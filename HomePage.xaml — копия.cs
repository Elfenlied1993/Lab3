using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Practices.ServiceLocation;
using MyGoalsPlan.Classes;
using MyGoalsPlan.Models;
using Newtonsoft.Json;
using Stc.Apps.Constants;
using Stc.Apps.Helpers.Helpers;
using Stc.Apps.Helpers.Models;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Color = Xamarin.Forms.Color;
using Point = Xamarin.Forms.Point;
using System.IO;
namespace MyGoalsPlan.Pages
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class HomePage : ContentPage
    {
        private readonly double _scaledScreenHeight = DeviceDisplay.MainDisplayInfo.Height / DeviceDisplay.MainDisplayInfo.Density;
        private readonly double _scaledScreenWidth = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;

        private readonly FontSizeHelper _fontSizeHelper;
        private List<GoalPlanItemModel> _viewModel;
        private List<SavedData> _savedData;
        /// <summary>
        /// Saved date in english.
        /// </summary>
        private string savedDate;
        /// <summary>
        /// App data.
        /// </summary>
        private ObservableCollection<Grouping<Section, Goal>> Data { get; set; }
        private readonly RESTApiHelper _restApiHelper;
        private readonly ILocationFetcher _locationFetcher;
        public GoogleHelper GoogleHelper { get; set; }
        private string ChangingValue { get; set; }
        private PointF CurrentOpened { get; set; }
        private string CurrentText { get; set; }
        private bool IsOpened { get; set; }
        public HomePage()
        {
            InitializeComponent();
            var test = DeviceDisplay.MainDisplayInfo;
            GoogleHelper = ServiceLocator.Current.GetInstance<GoogleHelper>();
            Data = new ObservableCollection<Grouping<Section, Goal>>();
            _savedData = new List<SavedData>();
            BindingContext = GoogleHelper;
            _locationFetcher = DependencyService.Get<ILocationFetcher>();
            _viewModel = new List<GoalPlanItemModel>();
            _restApiHelper = new RESTApiHelper();
            _fontSizeHelper = new FontSizeHelper();
            //Scaling height of XAML components to calculate font size of their children.
            GoalsDateContentView.WidthRequest = (int)(_scaledScreenWidth * 0.6);
            GoalsDate.WidthRequest = GoalsDateContentView.WidthRequest;
            Thread.CurrentThread.CurrentCulture = new CultureInfo(Settings.CurrentLanguage);
            GoalsDate.Text = DateTime.Today.ToString("MMMM yyyy");
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            savedDate = DateTime.Today.ToString("MMMM yyyy");
            Thread.CurrentThread.CurrentCulture = new CultureInfo(Settings.CurrentLanguage);
            ListGrid.WidthRequest = _scaledScreenWidth - 55;
            if (Settings.IsLogged == false)
            {
                _isLogged = false;
            }
            else
            {
                _isLogged = true;
            }
            if (Settings.PlanData != String.Empty)
            {
                _savedData = JsonConvert.DeserializeObject<List<SavedData>>(Settings.PlanData);
                foreach (var savedData in _savedData)
                {
                    var group = new ObservableCollection<Grouping<Section, Goal>>();
                    foreach (var section in savedData.Data)
                    {
                        group.Add(new Grouping<Section, Goal>(section, section.Goals));
                    }
                    _viewModel.Add(new GoalPlanItemModel()
                    {
                        Date = savedData.Date,
                        Data = group
                    });
                }
                Data = _viewModel.SingleOrDefault(x => x.Date == savedDate)?.Data ?? new ObservableCollection<Grouping<Section, Goal>>();
            }
            GoalsDate.FontSize = _fontSizeHelper.CalculateFontSize(GoalsDate.Text, _scaledScreenHeight * 0.2 * 0.5,
                GoalsDateContentView.WidthRequest * 0.8);
            if (Settings.CurrentLanguage.Contains("zh") || Settings.CurrentLanguage.Contains("ja"))
                GoalsDate.FontSize *= 1.4;
            if (Device.RuntimePlatform == Device.Android)
            {
                var screenOnClick = new TapGestureRecognizer();
                screenOnClick.Tapped += Screen_OnClick;
                MainAbsoluteLayout.GestureRecognizers.Add(screenOnClick);
            }
            LstView.ItemsSource = Data; // Hide navigation toolbar.
            AdjustListViewHeight();
            NavigationPage.SetHasNavigationBar(this, false);
        }

        #region buttons
        /// <summary>
        /// Changing visibility of hamburger menu to allow user to see the menu itself.
        /// </summary>
        private void hambuger_OnClick(object sender, EventArgs e)
        {
            if (Settings.UserData != string.Empty)
            {
                if (MenuSignedIn.IsVisible == false)
                {
                    CloseWindows();
                    MenuSignedIn.IsVisible = true;
                }
                else
                {
                    CloseWindows();
                    MenuSignedIn.IsVisible = false;
                }
            }
            else
            {
                if (MenuSignedOut.IsVisible == false)
                {
                    CloseWindows();
                    MenuSignedOut.IsVisible = true;
                }
                else
                {
                    CloseWindows();
                    MenuSignedOut.IsVisible = false;
                }
            }

        }
        /// <summary>
        /// Open dialog window to mail us.
        /// </summary>
        private async void BtnContactUs_Clicked(object sender, EventArgs e)
        {
            var receiverList = new List<string>();
            receiverList.Add("inspoapps@gmail.com");
            var message = new EmailMessage
            {
                Subject = "My Goal Plan - Support Ticket",
                To = receiverList,
                //Cc = ccRecipients,
                //Bcc = bccRecipients
            };
            await Email.ComposeAsync(message);
        }
        /// <summary>
        /// Using local storage data for an app.
        /// </summary>
        private async void BtnChoiceLocal_OnClick(object sender, EventArgs e)
        {
            Settings.IsUsingLocalStorage = true;
            ChoiceGrid.IsVisible = false;
            var thread = new Thread(() =>
            {
                while (Settings.IsUsingLocalStorage)
                {
                    if (Settings.CurrentAction == TranslateHelper.ResourceStrings()["ProgressActionSendDataText"])
                    {
                        Device.BeginInvokeOnMainThread(ChangeCurrentStatus);
                    }
                }
                Device.BeginInvokeOnMainThread(() =>
                {
                    Navigation.PushAsync(new HomePage());
                });
            });
            thread.Start();
            await _restApiHelper.SetData();

        }
        /// <summary>
        /// Using cloud data for an app.
        /// </summary>
        private async void BtnChoiceCloud_OnClick(object sender, EventArgs e)
        {
            Device.BeginInvokeOnMainThread(ChangeCurrentStatus);
            Settings.IsUsingLocalStorage = false;
            ChoiceGrid.IsVisible = false;
            Settings.CurrentAction = TranslateHelper.ResourceStrings()["ProgressActionReceiveDataText"];
            var userDataResponseBody = await _restApiHelper.GetData();
            if (userDataResponseBody.ResponseCode == ((int)ResponseCodes.GetDataSuccessful).ToString())
            {
                Settings.PlanData = userDataResponseBody.DataValue;
                await Navigation.PushAsync(new HomePage());
            }

            if (userDataResponseBody.ResponseCode == ((int)ResponseCodes.GetDataNoSuchKey).ToString())
            {
                await _restApiHelper.SetData();
                await Navigation.PushAsync(new HomePage());
            }
        }
        /// <summary>
        /// Navigate to SettingsPage. 
        /// </summary>
        private void BtnSettings_Clicked(object sender, EventArgs e)
        {
            Navigation.PushAsync(new SettingsPage());
        }
        /// <summary>
        /// Sign in via Google.
        /// </summary>
        private void BtnSignIn_Clicked(object sender, EventArgs e)
        {
            //Allowing only 1 tap.
            if (_userTappedSignIn)
                return;
            _userTappedSignIn = true;
            var current = Connectivity.NetworkAccess;
            if (current != NetworkAccess.Internet)
            {
                Navigation.PushAsync(new NoInternetPage());
            }
            else
            {
                CloseWindows();
                //Displaying progress window.
                Settings.CurrentAction = TranslateHelper.ResourceStrings()["ProgressActionSignInAwaitText"];
                ChangeCurrentStatus();
                GoogleHelper.GoogleLogin();
                //Starting background thread, where we updating progress info and awaiting result of signing in.
                Thread login = new Thread(() =>
                {

                    while (_isLogged == false)
                    {
                        if (Settings.CurrentAction == TranslateHelper.ResourceStrings()["ProgressActionSignInText"])
                        {
                            Device.BeginInvokeOnMainThread(ChangeCurrentStatus);
                        }

                        if (Settings.CurrentAction == TranslateHelper.ResourceStrings()["ProgressActionSignInErrorText"])
                        {
                            Device.BeginInvokeOnMainThread(ChangeCurrentStatus);
                            Device.BeginInvokeOnMainThread(() =>
                            {
                                if (Settings.CurrentAction == TranslateHelper.ResourceStrings()["ProgressActionSignInErrorText"])
                                {
                                    Thread.Sleep(1000);
                                    _isLogged = Settings.IsLogged;
                                    Navigation.PushAsync(new HomePage());
                                }
                            });
                            break;
                        }
                        if (Settings.CurrentAction.Contains(TranslateHelper.ResourceStrings()["ProgressActionSignInSuccessText"]))
                        {
                            Device.BeginInvokeOnMainThread(() =>
                            {
                                //Displaying storage choice window.
                                CheckData();
                            });
                            break;
                        }
                        if (Settings.CurrentAction == TranslateHelper.ResourceStrings()["ProgressActionReceiveDataText"])
                        {
                            Device.BeginInvokeOnMainThread(ChangeCurrentStatus);
                        }
                        if (Settings.CurrentAction == TranslateHelper.ResourceStrings()["ProgressActionReceiveUserInfoText"])
                        {
                            Device.BeginInvokeOnMainThread(ChangeCurrentStatus);
                        }
                    }

                });
                login.Start();
            }
        }
        /// <summary>
        /// Changing visibility of add section menu to allow user to see the menu itself.
        /// </summary>
        private void AddSection_OnClicked(object sender, EventArgs e)
        {
            AddSectionMenu.IsVisible = !AddSectionMenu.IsVisible;
        }
        /// <summary>
        /// Changing visibility of section menu to allow user to see the menu itself.
        /// </summary>
        private void SectionMenu_OnClick(object sender, EventArgs e)
        {
            CloseWindows();
            var imageButton = (ImageButton)sender;
            var stackLayout = (StackLayout)imageButton.Parent;
            var label = (Label)stackLayout.Children[0];
            var location = _locationFetcher.GetCoordinates(imageButton);
            if (location == PointF.Empty)
            {
                location = new PointF((float)(_scaledScreenWidth * 0.25), (float)(_scaledScreenHeight * 0.25));
            }
            var boxViewWidth = _scaledScreenWidth * 0.5;
            var boxViewHeight = _scaledScreenHeight * 0.35;
            if (location.X > _scaledScreenWidth * 0.5)
                location.X -= (float)boxViewWidth;
            if (location.Y > _scaledScreenHeight * 0.65)
                location.Y -= (float)boxViewHeight;
            var labelRename = new Label
            {
                BackgroundColor = Color.Transparent,
                HeightRequest = _scaledScreenHeight * 0.07,
                Style = (Style)Application.Current.Resources["LabelTitleStyle"],
                Text = TranslateHelper.ResourceStrings()["HomeSectionRenameText"],
                FontSize = _fontSizeHelper.CalculateFontSize(
                    TranslateHelper.ResourceStrings()["HomeGoalRenameText"],
                    boxViewHeight * 0.15,
                    boxViewWidth * 0.65),
                GestureRecognizers = { new TapGestureRecognizer
                {
                Command = new Command(()=>Rename(label))
            } }
            };
            var labelDelete = new Label
            {
                BackgroundColor = Color.Transparent,
                Style = (Style)Application.Current.Resources["LabelTitleStyle"],
                Text = TranslateHelper.ResourceStrings()["HomeSectionDeleteText"],
                HeightRequest = _scaledScreenHeight * 0.07,
                FontSize = labelRename.FontSize,
                GestureRecognizers = { new TapGestureRecognizer()
                {
                Command = new Command(()=>Delete(label))
            }}
            };
            var labelMoveUp = new Label
            {
                BackgroundColor = Color.Transparent,
                Style = (Style)Application.Current.Resources["LabelTitleStyle"],
                Text = TranslateHelper.ResourceStrings()["HomeSectionMoveUpText"],
                HeightRequest = _scaledScreenHeight * 0.07,
                FontSize = _fontSizeHelper.CalculateFontSize(
                    TranslateHelper.ResourceStrings()["HomeSectionMoveUpText"],

                    boxViewHeight * 0.175,
                    boxViewWidth * 0.65),
                GestureRecognizers = { new TapGestureRecognizer()
                {
                    Command = new Command(()=>Move(label,"up"))
                }}
            };
            if (labelMoveUp.FontSize > labelRename.FontSize)
                labelMoveUp.FontSize = labelRename.FontSize;
            var labelMoveDown = new Label
            {
                BackgroundColor = Color.Transparent,
                Style = (Style)Application.Current.Resources["LabelTitleStyle"],
                Text = TranslateHelper.ResourceStrings()["HomeSectionMoveDownText"],
                HeightRequest = _scaledScreenHeight * 0.07,
                FontSize = _fontSizeHelper.CalculateFontSize(
                    TranslateHelper.ResourceStrings()["HomeSectionMoveDownText"],
                    boxViewHeight * 0.175,
                     boxViewWidth * 0.65),
                GestureRecognizers = { new TapGestureRecognizer()
                {
                    Command = new Command(()=>Move(label,"down"))
                }}
            };
            if (labelMoveDown.FontSize > labelRename.FontSize)
                labelMoveDown.FontSize = labelRename.FontSize;
            var labelAddGoal = new Label
            {
                BackgroundColor = Color.Transparent,
                Style = (Style)Application.Current.Resources["LabelTitleStyle"],
                Text = TranslateHelper.ResourceStrings()["HomeSectionAddText"],
                HeightRequest = _scaledScreenHeight * 0.07,
                FontSize = _fontSizeHelper.CalculateFontSize(
                    TranslateHelper.ResourceStrings()["HomeSectionAddText"],
                    boxViewHeight * 0.175,
                    boxViewWidth * 0.65),
                GestureRecognizers = { new TapGestureRecognizer()
                {
                    Command = new Command(()=>AddGoal(label))
                }}
            };
            if (labelAddGoal.FontSize > labelRename.FontSize)
                labelAddGoal.FontSize = labelRename.FontSize;
            var boxView = new BoxView
            {
                BackgroundColor = Color.Black,
                Opacity = 0.7,
                WidthRequest = boxViewWidth,
                HeightRequest = boxViewHeight,
                AnchorY = location.Y,
                AnchorX = location.X
            };
            var boxViewPosition = new Point(location.X + 20, location.Y + 20);
            var renamePosition = new Point(location.X + 30, location.Y + 30);
            var deletePosition = new Point(renamePosition.X, renamePosition.Y + labelRename.HeightRequest);
            var moveUpPosition = new Point(deletePosition.X, deletePosition.Y + labelDelete.HeightRequest);
            var moveDownPosition = new Point(moveUpPosition.X, moveUpPosition.Y + labelMoveUp.HeightRequest);
            var newGoalPosition = new Point(moveDownPosition.X, moveDownPosition.Y + labelMoveDown.HeightRequest);
            for (int i = 11; i < MainAbsoluteLayout.Children.Count;)
            {
                MainAbsoluteLayout.Children.RemoveAt(i);
            }
            if (CurrentOpened != location)
            {
                MainAbsoluteLayout.Children.Add(boxView, boxViewPosition);
                MainAbsoluteLayout.Children.Add(labelRename, renamePosition);
                MainAbsoluteLayout.Children.Add(labelDelete, deletePosition);
                MainAbsoluteLayout.Children.Add(labelMoveUp, moveUpPosition);
                MainAbsoluteLayout.Children.Add(labelMoveDown, moveDownPosition);
                MainAbsoluteLayout.Children.Add(labelAddGoal, newGoalPosition);
                CurrentOpened = location;
                IsOpened = true;
            }
            if (CurrentOpened == location)
            {
                if (IsOpened == false)
                    CurrentOpened = PointF.Empty;
                IsOpened = false;
            }
        }
        /// <summary>
        /// Changing visibility of goal menu to allow user to see the menu itself.
        /// </summary>
        private void GoalMenu_OnClick(object sender, EventArgs e)
        {
            CloseWindows();
            var imageButton = (ImageButton)sender;
            var stackLayout = (StackLayout)imageButton.Parent;
            var label = (Label)stackLayout.Children[1];
            var absoluteLayout = (AbsoluteLayout)stackLayout.Parent;
            var viewCell = (ViewCell)absoluteLayout.Parent;
            var location = _locationFetcher.GetCoordinates(imageButton);
            if (location == PointF.Empty)
            {
                location = new PointF((float)(_scaledScreenWidth * 0.25), (float)(_scaledScreenHeight * 0.25));
            }
            var boxViewHeight = _scaledScreenHeight * 0.3;
            var boxViewWidth = _scaledScreenWidth * 0.5;
            if (location.X > _scaledScreenWidth * 0.5)
                location.X -= (float)boxViewWidth;
            if (location.Y > _scaledScreenHeight * 0.7)
                location.Y -= (float)boxViewHeight;
            var labelRename = new Label
            {
                BackgroundColor = Color.Transparent,
                HeightRequest = _scaledScreenHeight * 0.075,
                Style = (Style)Application.Current.Resources["LabelTitleStyle"],
                Text = TranslateHelper.ResourceStrings()["HomeGoalRenameText"],
                FontSize = _fontSizeHelper.CalculateFontSize(
                    TranslateHelper.ResourceStrings()["HomeGoalRenameText"],
                    boxViewHeight * 0.175,
                    boxViewWidth * 0.65),
                GestureRecognizers = { new TapGestureRecognizer
                {
                    Command = new Command(()=>Rename(label))
                }}
            };
            var labelDelete = new Label
            {
                BackgroundColor = Color.Transparent,
                Style = (Style)Application.Current.Resources["LabelTitleStyle"],
                Text = TranslateHelper.ResourceStrings()["HomeGoalDeleteText"],
                HeightRequest = _scaledScreenHeight * 0.075,
                FontSize = labelRename.FontSize,
                GestureRecognizers = { new TapGestureRecognizer()
                {
                    Command = new Command(()=>Delete(label))
                }}
            };
            var labelMoveUp = new Label
            {
                BackgroundColor = Color.Transparent,
                Style = (Style)Application.Current.Resources["LabelTitleStyle"],
                Text = TranslateHelper.ResourceStrings()["HomeGoalMoveUpText"],
                HeightRequest = _scaledScreenHeight * 0.075,
                FontSize = _fontSizeHelper.CalculateFontSize(
                    TranslateHelper.ResourceStrings()["HomeGoalMoveUpText"],
                    boxViewHeight * 0.175,
                    boxViewWidth * 0.65),
                GestureRecognizers = { new TapGestureRecognizer()
                {
                Command = new Command(()=>Move(label,"up"))
                }}
            };
            if (labelMoveUp.FontSize > labelRename.FontSize)
                labelMoveUp.FontSize = labelRename.FontSize;
            var labelMoveDown = new Label
            {
                BackgroundColor = Color.Transparent,
                Style = (Style)Application.Current.Resources["LabelTitleStyle"],
                Text = TranslateHelper.ResourceStrings()["HomeGoalMoveDownText"],
                HeightRequest = _scaledScreenHeight * 0.075,
                FontSize = _fontSizeHelper.CalculateFontSize(
                    TranslateHelper.ResourceStrings()["HomeGoalMoveDownText"],
                    boxViewHeight * 0.175,
                    boxViewWidth * 0.65),
                GestureRecognizers = { new TapGestureRecognizer()
                {
                        Command = new Command(()=>Move(label,"down"))
                }}
            };
            if (labelMoveDown.FontSize > labelRename.FontSize)
                labelMoveDown.FontSize = labelRename.FontSize;
            var boxView = new BoxView
            {
                BackgroundColor = Color.Black,
                Opacity = 0.7,
                WidthRequest = boxViewWidth,
                HeightRequest = boxViewHeight,
                AnchorY = location.Y,
                AnchorX = location.X
            };
            var boxViewPosition = new Point(location.X + 20, location.Y + 20);
            var renamePosition = new Point(location.X + 30, location.Y + 30);
            var deletePosition = new Point(renamePosition.X, renamePosition.Y + labelRename.HeightRequest);
            var moveUpPosition = new Point(deletePosition.X, deletePosition.Y + labelDelete.HeightRequest);
            var moveDownPosition = new Point(moveUpPosition.X, moveUpPosition.Y + labelMoveUp.HeightRequest);
            for (int i = 11; i < MainAbsoluteLayout.Children.Count;)
            {
                MainAbsoluteLayout.Children.RemoveAt(i);
            }
            if (CurrentOpened != location || CurrentText != label.Text)
            {
                MainAbsoluteLayout.Children.Add(boxView, boxViewPosition);
                MainAbsoluteLayout.Children.Add(labelRename, renamePosition);
                MainAbsoluteLayout.Children.Add(labelDelete, deletePosition);
                MainAbsoluteLayout.Children.Add(labelMoveUp, moveUpPosition);
                MainAbsoluteLayout.Children.Add(labelMoveDown, moveDownPosition);
                CurrentOpened = location;
                IsOpened = true;
                CurrentText = label.Text;
            }
            if (CurrentOpened == location)
            {
                if (IsOpened == false)
                    CurrentOpened = PointF.Empty;
                IsOpened = false;
            }
        }
        /// <summary>
        /// Open web browser to open App page in play market/app store.
        /// </summary>
        private void BtnRate_Clicked(object sender, EventArgs e)
        {
            var uri = new Uri("https://play.google.com/store/apps/details?id=com.inspoapps.mygoalsplan");
            Device.OpenUri(uri);
        }
        /// <summary>
        /// Navigate to PrivacyPolicyPage.
        /// </summary>
        private void BtnPrivacyPolicy_Clicked(object sender, EventArgs e)
        {
            //Check if menu is visible.

            Navigation.PushAsync(new PrivacyPolicyPage());
        }
        /// <summary>
        /// Navigate to TermsOfUsePage.
        /// </summary>
        private void BtnTermsOfUse_Clicked(object sender, EventArgs e)
        {
            //Check if menu is visible.
            Navigation.PushAsync(new TermsOfUsePage());
        }
        /// <summary>
        /// Navigate to AboutTheAppPage.
        /// </summary>
        private void BtnAboutTheApp_Clicked(object sender, EventArgs e)
        {
            //Check if menu is visible.
            Navigation.PushAsync(new AboutTheAppPage());
        }
        /// <summary>
        /// Navigate to ProfilePage.
        /// </summary>
        private void BtnProfile_Clicked(object sender, EventArgs e)
        {
            Navigation.PushAsync(new ProfilePage());
        }
        /// <summary>
        /// Turning off navigation to LoginPage.
        /// </summary>
        protected override bool OnBackButtonPressed()
        {
            return true;
        }
        /// <summary>
        /// Logout from Google and server.
        /// </summary>
        private async void BtnLogout_Clicked(object sender, EventArgs e)
        {
            //Allowing only 1 tap.
            if (_userTappedLogout)
                return;
            _userTappedLogout = true;
            var current = Connectivity.NetworkAccess;
            if (current != NetworkAccess.Internet)
            {
                await Navigation.PushAsync(new NoInternetPage());
            }
            else
            {
                CloseWindows();
                //Displaying progress window.
                Settings.CurrentAction = TranslateHelper.ResourceStrings()["ProgressActionLogoutAwaitText"];
                StatusText.Text = Settings.CurrentAction;
                StatusText.FontSize = _fontSizeHelper.CalculateFontSize(StatusText.Text, _scaledScreenHeight * 0.1,
                    _scaledScreenWidth * 0.3);
                StatusLayout.IsVisible = true;
                await StatusLayout.FadeTo(1, 250, Easing.Linear);
                _viewModel = new List<GoalPlanItemModel>();
                //Starting background thread, where we updating progress info and awaiting result of logout.
                Thread logout = new Thread(() =>
                {
                    while (IsLogged)
                    {
                        if (Settings.CurrentAction == TranslateHelper.ResourceStrings()["ProgressActionSendDataText"])
                        {
                            Device.BeginInvokeOnMainThread(ChangeCurrentStatus);
                        }

                        if (Settings.CurrentAction == TranslateHelper.ResourceStrings()["ProgressActionLogoutText"])
                        {
                            Device.BeginInvokeOnMainThread(ChangeCurrentStatus);
                        }

                        if (Settings.CurrentAction == TranslateHelper.ResourceStrings()["ProgressActionLogoutSuccessText"])
                        {
                            Device.BeginInvokeOnMainThread(ChangeCurrentStatus);
                        }
                        IsLogged = Settings.IsLogged;
                    }
                });
                await Task.Run(logout.Start);
                await _restApiHelper.Logout();
                Task.Run(() => GoogleHelper.GoogleLogout()).Wait();
                Settings.IsLogged = false;
                await Navigation.PushAsync(new HomePage());
            }
        }
        #endregion
        #region prop
        private bool _userTappedLogout;
        private bool _userTappedSignIn;
        private string _currState;
        private bool _isLogged;
        public bool IsLogged
        {
            get => _isLogged;
            set
            {
                if (_isLogged != value)
                {
                    _isLogged = value;
                }
            }
        }
        public string CurrState
        {
            get => _currState;
            set
            {
                _currState = value;
                OnPropertyChanged();
            }
        }

        #endregion
        #region events
        /// <summary>
        /// Define imageButton's first state as default.
        /// </summary>
        protected override void OnAppearing()
        {
            CurrState = CheckButtonState.Default;
        }
        /// <summary>
        /// Saving data when switching app/page.
        /// </summary>
        protected override void OnDisappearing()
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            if (!_viewModel.Exists(x => x.Date == GoalsDate.Text))
                _viewModel.Add(new GoalPlanItemModel()
                {
                    Date = GoalsDate.Text,
                    Data = Data
                });
            Thread.CurrentThread.CurrentCulture = new CultureInfo(Settings.CurrentLanguage);
            SaveData();
        }
        /// <summary>
        /// Calculating font sizes with proportions of screen size and text's length.
        /// </summary>
        private void Label_OnSizeChanged(object sender, EventArgs e)
        {
            MenuTermsText.Text = TranslateHelper.ResourceStrings()["HomeMenuTermsText"];
            MenuContactText.Text = TranslateHelper.ResourceStrings()["HomeMenuContactText"];
            MenuAboutText.Text = TranslateHelper.ResourceStrings()["HomeMenuAboutText"];
            MenuPrivacyText.Text = TranslateHelper.ResourceStrings()["HomeMenuPrivacyText"];
            MenuRateText.Text = TranslateHelper.ResourceStrings()["HomeMenuRateText"];
            MenuSignInText.Text = TranslateHelper.ResourceStrings()["HomeMenuSignInText"];
            MenuAboutSignedInText.Text = TranslateHelper.ResourceStrings()["HomeMenuAboutText"];
            MenuContactSignedInText.Text = TranslateHelper.ResourceStrings()["HomeMenuContactText"];
            MenuPrivacySignedInText.Text = TranslateHelper.ResourceStrings()["HomeMenuPrivacyText"];
            MenuTermsSignedInText.Text = TranslateHelper.ResourceStrings()["HomeMenuTermsText"];
            MenuRateSignedInText.Text = TranslateHelper.ResourceStrings()["HomeMenuRateText"];
            MenuLogoutText.Text = TranslateHelper.ResourceStrings()["HomeMenuLogoutText"];
            MenuSettingsSignedInText.Text = TranslateHelper.ResourceStrings()["HomeMenuSettingsText"];
            MenuProfileText.Text = TranslateHelper.ResourceStrings()["HomeMenuProfileText"];
            MenuSettingsText.Text = TranslateHelper.ResourceStrings()["HomeMenuSettingsText"];
            ChoiceText.Text = TranslateHelper.ResourceStrings()["HomeChoiceText"];
            ChoiceCloud.Text = TranslateHelper.ResourceStrings()["HomeChoiceCloud"];
            ChoiceLocal.Text = TranslateHelper.ResourceStrings()["HomeChoiceLocal"];
            AddSectionText.Text = TranslateHelper.ResourceStrings()["HomeAddSectionText"];
            AddSectionEntryTitle.Text = TranslateHelper.ResourceStrings()["HomeAddSectionEntryTitle"];
            AddSectionOkButton.Text = TranslateHelper.ResourceStrings()["HomeOkButton"];
            AddSectionCancelButton.Text = TranslateHelper.ResourceStrings()["HomeCancelButton"];
            RenameCancelButton.Text = TranslateHelper.ResourceStrings()["HomeCancelButton"];
            RenameOkButton.Text = TranslateHelper.ResourceStrings()["HomeOkButton"];
            RenameEntryTitle.Text = TranslateHelper.ResourceStrings()["HomeRenameEntryTitle"];
            AddGoalCancelButton.Text = TranslateHelper.ResourceStrings()["HomeCancelButton"];
            AddGoalOkButton.Text = TranslateHelper.ResourceStrings()["HomeOkButton"];
            AddGoalEntryTitle.Text = TranslateHelper.ResourceStrings()["HomeAddGoalEntryTitle"];
            MenuTermsText.FontSize = _fontSizeHelper.CalculateFontSize(MenuTermsText.Text, _scaledScreenHeight * 0.05, (_scaledScreenWidth - 90) * 0.7);
            MenuContactText.FontSize = MenuTermsText.FontSize;
            MenuAboutText.FontSize = MenuTermsText.FontSize;
            MenuPrivacyText.FontSize = MenuTermsText.FontSize;
            MenuRateText.FontSize = MenuTermsText.FontSize;
            MenuSignInText.FontSize = MenuTermsText.FontSize;
            MenuAboutSignedInText.FontSize = MenuTermsText.FontSize;
            MenuContactSignedInText.FontSize = MenuTermsText.FontSize;
            MenuPrivacySignedInText.FontSize = MenuTermsText.FontSize;
            MenuTermsSignedInText.FontSize = MenuTermsText.FontSize;
            MenuRateSignedInText.FontSize = MenuTermsText.FontSize;
            MenuLogoutText.FontSize = MenuTermsText.FontSize;
            MenuProfileText.FontSize = MenuTermsText.FontSize;
            MenuSettingsText.FontSize = MenuTermsText.FontSize;
            MenuSettingsSignedInText.FontSize = MenuTermsText.FontSize;
            AddSectionEntry.FontSize = MenuTermsText.FontSize;
            AddSectionText.FontSize = MenuTermsText.FontSize *0.9;
            AddSectionEntryTitle.FontSize = MenuTermsText.FontSize * 0.7;
            AddSectionOkButton.FontSize = AddSectionEntryTitle.FontSize;
            AddSectionCancelButton.FontSize = AddSectionEntryTitle.FontSize;
            RenameCancelButton.FontSize = AddSectionCancelButton.FontSize;
            RenameEntry.FontSize = AddSectionEntry.FontSize;
            RenameOkButton.FontSize = AddSectionOkButton.FontSize;
            RenameEntryTitle.FontSize = AddSectionEntry.FontSize;
            AddGoalCancelButton.FontSize = AddSectionCancelButton.FontSize;
            AddGoalEntry.FontSize = AddSectionEntry.FontSize;
            AddGoalOkButton.FontSize = AddSectionOkButton.FontSize;
            AddGoalEntryTitle.FontSize = AddSectionEntry.FontSize;
            var rows = _fontSizeHelper.CalculateRowsAmount(MenuPrivacySignedInText.Text,
                _scaledScreenHeight * 0.05, _scaledScreenWidth * 0.6,
                MenuPrivacySignedInText.FontSize);
            if (rows > 1.1)
            {
                PrivacyRow.Height = new GridLength(rows * 1.3, GridUnitType.Star);
                PrivacySignedInRow.Height = new GridLength(rows * 1.3, GridUnitType.Star);
            }
            rows = _fontSizeHelper.CalculateRowsAmount(MenuRateText.Text,
                _scaledScreenHeight * 0.05, _scaledScreenWidth * 0.63,
                MenuRateText.FontSize);
            if (rows > 1.1)
            {
                RateRow.Height = new GridLength(rows * 1.4, GridUnitType.Star);
                RateSignedInRow.Height = new GridLength(rows * 1.4, GridUnitType.Star);
            }
            ChoiceText.FontSize = _fontSizeHelper.CalculateFontSize(ChoiceText.Text, _scaledScreenHeight * 0.12,
                _scaledScreenWidth * 0.6);
            ChoiceCloud.FontSize = _fontSizeHelper.CalculateFontSize(ChoiceCloud.Text, _scaledScreenHeight * 0.06,
                _scaledScreenWidth * 0.3);
            ChoiceLocal.FontSize = ChoiceCloud.FontSize;
        }
        /// <summary>
        /// Switching current imageButton source and label TextDecorations when tapping on viewcell.
        /// </summary>
        private void ViewCellName_OnTapped(object sender, EventArgs e)
        {
            var viewCell = (ViewCell)sender;
            var absoluteLayout = (AbsoluteLayout)viewCell.LogicalChildren[0];
            var stackLayout = (StackLayout)absoluteLayout.Children[0];
            var imageStackLayout = (StackLayout)stackLayout.Children[0];
            var imageButton = (ImageButton)imageStackLayout.Children[0];
            var label = (Label)stackLayout.Children[1];
            var source = imageButton.Source.ToString();
            source = source.Replace("File: ", "");
            imageButton.Source = source == CheckButtonState.Default ? CheckButtonState.Pressed : CheckButtonState.Default;
            var goals = Data.Select(x => x.Key.Goals).FirstOrDefault(x => x.FirstOrDefault(s => s.GoalName == label.Text)?.GoalName == label.Text);
            if (source == CheckButtonState.Default)
            {
                goals.ElementAt(0).IsFinished = true;
                imageButton.Source = CheckButtonState.Pressed;
                label.TextDecorations = TextDecorations.Strikethrough;

            }
            else
            {
                goals.ElementAt(0).IsFinished = false;
                imageButton.Source = CheckButtonState.Default;
                label.TextDecorations = TextDecorations.None;
            }
            SaveData();

        }
        /// <summary>
        /// Checking data on cloud.
        /// If empty - saving local data to the cloud and reloading page, else displaying data choice window.
        /// </summary>
        private async void CheckData()
        {
            var userDataResponseBody = await _restApiHelper.GetData();
            if (userDataResponseBody.ResponseCode == ((int)ResponseCodes.GetDataSuccessful).ToString())
            {
                ChoiceGrid.IsVisible = true;
            }
            if (userDataResponseBody.ResponseCode == ((int)ResponseCodes.GetDataNoSuchKey).ToString())
            {
                await _restApiHelper.SetData();
                await Navigation.PushAsync(new HomePage());
            }
        }
        /// <summary>
        /// Close all pop up windows when screen has been clicked.
        /// </summary>
        private void Screen_OnClick(object sender, EventArgs e)
        {
            CloseWindows();
        }
        /// <summary>
        /// Displaying popup menu where user can create new section.
        /// </summary>
        private void LabelAddSection_Clicked(object sender, EventArgs e)
        {
            CloseWindows();
            AddSectionEntryGrid.IsVisible = !AddSectionEntryGrid.IsVisible;
        }
        /// <summary>
        /// Adding and saving new section.
        /// </summary>
        private void EntrySectionOk_Tapped(object sender, EventArgs e)
        {
            if (AddSectionEntry.Text != null)
            {
                if ((Data.FirstOrDefault(x => x.Key.SectionName == AddSectionEntry.Text)?.Key?.SectionName ??
                     String.Empty) != AddSectionEntry.Text)
                {
                    var goals = Data.Select(x => x.Key.Goals).FirstOrDefault(x =>
                        x.FirstOrDefault(s => s.GoalName == AddSectionEntry.Text)?.GoalName == AddSectionEntry.Text);
                    if (goals == null)
                    {
                        var section = new Section()
                        {
                            SectionName = AddSectionEntry.Text,
                            Goals = new List<Goal>()
                        };
                        var group = new Grouping<Section, Goal>(section, section.Goals);
                        Data.Add(group);
                        LstView.ItemsSource = Data;
                    }

                }
            }
            AdjustListViewHeight();
            SaveData();
            AddSectionEntry.Unfocus();
            AddSectionEntry.Text = null;
            AddSectionEntryGrid.IsVisible = false;
        }
        /// <summary>
        /// Hiding popup menu.
        /// </summary>
        private void EntrySectionCancel_Tapped(object sender, EventArgs e)
        {
            AddSectionEntry.Text = null;
            AddSectionEntry.Unfocus();
            AddSectionEntryGrid.IsVisible = false;
        }
        /// <summary>
        /// Changing current month to the next one.
        /// </summary>
        private void Screen_SwipedLeft(object sender, SwipedEventArgs e)
        {
            CloseWindows();
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            if (!_viewModel.Exists(x => x.Date == GoalsDate.Text))
                _viewModel.Add(new GoalPlanItemModel()
                {
                    Date = GoalsDate.Text,
                    Data = Data
                });
            Thread.CurrentThread.CurrentCulture = new CultureInfo(Settings.CurrentLanguage);
            Data = new ObservableCollection<Grouping<Section, Goal>>();
            LstView.ItemsSource = null;
            var dateTime = Convert.ToDateTime(savedDate);
            GoalsDate.Text = dateTime.AddMonths(1).ToString("MMMM yyyy");
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            savedDate = dateTime.AddMonths(1).ToString("MMMM yyyy");
            Thread.CurrentThread.CurrentCulture = new CultureInfo(Settings.CurrentLanguage);
            Data = _viewModel.SingleOrDefault(x => x.Date == savedDate)?.Data ?? new ObservableCollection<Grouping<Section, Goal>>();
            LstView.ItemsSource = Data;
            AdjustListViewHeight();
        }
        /// <summary>
        /// Changing current month to the previous one.
        /// </summary>
        private void Screen_SwipedRight(object sender, SwipedEventArgs e)
        {
            CloseWindows();
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            if (!_viewModel.Exists(x => x.Date == GoalsDate.Text))
                _viewModel.Add(new GoalPlanItemModel()
                {
                    Date = GoalsDate.Text,
                    Data = Data
                });
            Thread.CurrentThread.CurrentCulture = new CultureInfo(Settings.CurrentLanguage);
            Data = new ObservableCollection<Grouping<Section, Goal>>();
            LstView.ItemsSource = null;
            var dateTime = Convert.ToDateTime(savedDate);
            GoalsDate.Text = dateTime.AddMonths(-1).ToString("MMMM yyyy");
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            savedDate = dateTime.AddMonths(-1).ToString("MMMM yyyy");
            Thread.CurrentThread.CurrentCulture = new CultureInfo(Settings.CurrentLanguage);
            Data = _viewModel.SingleOrDefault(x => x.Date == savedDate)?.Data ?? new ObservableCollection<Grouping<Section, Goal>>();
            LstView.ItemsSource = Data;
            AdjustListViewHeight();

        }
        /// <summary>
        /// Calculating font sizes with proportions of screen size and text's length.
        /// </summary>
        private void StackLayout_OnBindingContextChanged(object sender, EventArgs e)
        {
            var stackLayout = (StackLayout)sender;
            var sectionStackLayout = (StackLayout)stackLayout.Children[0];
            var label = (Label)sectionStackLayout.Children[0];
            label.FontSize=GoalsDate.FontSize*0.9;
        }
        /// <summary>
        /// Calculating font sizes with proportions of screen size and text's length.
        /// </summary>
        private void Goals_OnBindingContextChanged(object sender, EventArgs e)
        {
            var absoluteLayout = (AbsoluteLayout)sender;
            var stackLayout = (StackLayout)absoluteLayout.Children[0];
            var label = (Label)stackLayout.Children[1];
            label.FontSize = GoalsDate.FontSize * 0.5;
        }
        /// <summary>
        /// Switching current imageButton source and label TextDecorations when tapping on button.
        /// </summary
        private void ImageButton_OnClick(object sender, EventArgs e)
        {
            var imageButton = (ImageButton)sender;
            var imageStackLayout = (StackLayout)imageButton.Parent;
            var stackLayout = (StackLayout)imageStackLayout.Parent;
            var source = imageButton.Source.ToString();
            var label = (Label)stackLayout.Children[1];
            source = source.Replace("File: ", "");
            imageButton.Source = source == CheckButtonState.Default ? CheckButtonState.Pressed : CheckButtonState.Default;
            var goals = Data.Select(x => x.Key.Goals).FirstOrDefault(x => x.FirstOrDefault(s => s.GoalName == label.Text)?.GoalName == label.Text);
            if (source == CheckButtonState.Default)
            {
                goals.ElementAt(0).IsFinished = true;
                imageButton.Source = CheckButtonState.Pressed;
                label.TextDecorations = TextDecorations.Strikethrough;

            }
            else
            {
                goals.ElementAt(0).IsFinished = false;
                imageButton.Source = CheckButtonState.Default;
                label.TextDecorations = TextDecorations.None;
            }
            SaveData();
        }
        /// <summary>
        /// Adding and saving new goal.
        /// </summary>
        private void AddGoalOk_Tapped(object sender, EventArgs e)
        {
            if (AddGoalEntry.Text != null)
            {
                var goals = Data.Select(x => x.Key.Goals).FirstOrDefault(x => x.FirstOrDefault(s => s.GoalName == AddGoalEntry.Text)?.GoalName == AddGoalEntry.Text);
                if (goals == null)
                {
                    var section = Data.Single(x => x.Key.SectionName.Equals(ChangingValue));
                    var index = Data.IndexOf(section);
                    var goalList = section.Key.Goals;
                    goalList.Add(new Goal()
                    {
                        GoalName = AddGoalEntry.Text,
                        IsFinished = false
                    });
                    var group = new Grouping<Section, Goal>(section.Key, goalList);
                    Data.RemoveAt(index);
                    Data.Insert(index, group);
                }
            }
            AdjustListViewHeight();
            AddGoalEntry.Text = null;
            AddGoalEntry.Unfocus();
            AddGoalEntryGrid.IsVisible = false;
            SaveData();
        }
        /// <summary>
        /// Hiding popup menu.
        /// </summary>
        private void AddGoalCancel_Tapped(object sender, EventArgs e)
        {
            AddGoalEntry.Text = null;
            AddGoalEntry.Unfocus();
            AddGoalEntryGrid.IsVisible = false;
        }
        /// <summary>
        /// Renaming section/goal.
        /// </summary>
        private void RenameOk_Tapped(object sender, EventArgs e)
        {
            if (RenameEntry.Text != null)
            {
                foreach (var section in Data)
                {
                    if (section.Key.SectionName == ChangingValue)
                    {
                        if ((Data.FirstOrDefault(x => x.Key.SectionName == RenameEntry.Text)?.Key?.SectionName ?? String.Empty) != RenameEntry.Text)
                            section.Key.SectionName = RenameEntry.Text;
                        break;
                    }
                    foreach (var goal in section)
                    {
                        if (goal.GoalName == ChangingValue)
                        {
                            var goals = Data.Select(x => x.Key.Goals).FirstOrDefault(x => x.FirstOrDefault(s => s.GoalName == RenameEntry.Text)?.GoalName == RenameEntry.Text);
                            if (goals == null)
                            {
                                goal.GoalName = RenameEntry.Text;
                                break;
                            }
                        }
                    }
                }
                OnPropertyChanged();
            }
            SaveData();
            RenameEntry.Text = null;
            RenameEntry.Unfocus();
            RenameEntryGrid.IsVisible = !RenameEntryGrid.IsVisible;
        }
        /// <summary>
        /// Hiding popup menu
        /// </summary>
        private void RenameCancel_Tapped(object sender, EventArgs e)
        {
            RenameEntry.Text = null;
            RenameEntry.Unfocus();
            RenameEntryGrid.IsVisible = false;
        }
        #endregion
        #region functions
        /// <summary>
        /// Deleting goal/section.
        /// </summary>
        /// <param name="label">Goal/section which will be deleted.</param>
        private void Delete(Label label)
        {
            CloseWindows();
            ChangingValue = label.Text;
            foreach (var section in Data)
            {
                if (section.Key.SectionName == ChangingValue)
                {
                    Data.Remove(section);
                    break;
                }
                foreach (var goal in section)
                {
                    if (goal.GoalName == ChangingValue)
                    {
                        section.Remove(goal);
                        section.Key.Goals.Remove(goal);
                        break;
                    }
                }
            }
            AdjustListViewHeight();
            SaveData();
        }
        /// <summary>
        /// Renaming goal/section.
        /// </summary>
        /// <param name="label">Goal/section which will be renamed.</param>
        private void Rename(Label label)
        {
            CloseWindows();
            ChangingValue = label.Text;
            RenameEntry.Text = label.Text;
            RenameEntryGrid.IsVisible = !RenameEntryGrid.IsVisible;
        }
        /// <summary>
        /// Moving goal/section
        /// </summary>
        /// <param name="label">Goal/section which will be moved.</param>
        /// <param name="orientation">Where to move(up/down).</param>
        private void Move(Label label, string orientation)
        {
            CloseWindows();
            ChangingValue = label.Text;
            var data = Data;
            if (orientation == "up")
            {
                foreach (var section in Data)
                {
                    if (section.Key.SectionName == ChangingValue)
                    {
                        var index = Data.IndexOf(section);
                        if (index != 0)
                        {
                            var previousSection = Data.ElementAt(index - 1);
                            Data.Remove(section);
                            Data.Remove(previousSection);
                            Data.Insert(index - 1, section);
                            Data.Insert(index, previousSection);
                            //Data.Move(index, index - 1);
                        }
                        return;
                    }
                    foreach (var goal in section)
                    {
                        if (goal.GoalName == ChangingValue)
                        {
                            var index = section.IndexOf(goal);
                            if (index != 0)
                            {
                                section.Move(index, index - 1);
                                var sectionIndex = Data.IndexOf(section);
                                Data.Remove(section);
                                Data.Insert(sectionIndex, section);
                            }
                            return;
                        }
                    }
                }
            }
            else
            {
                foreach (var section in Data)
                {
                    if (section.Key.SectionName == ChangingValue)
                    {
                        var index = Data.IndexOf(section);
                        if (index < Data.Count - 1)
                        {
                            var previousSection = Data.ElementAt(index + 1);
                            Data.Remove(section);
                            Data.Remove(previousSection);
                            Data.Insert(index, previousSection);
                            Data.Insert(index + 1, section);
                            //Data.Move(index, index + 1);
                        }
                        return;
                    }
                    foreach (var goal in section)
                    {
                        if (goal.GoalName == ChangingValue)
                        {
                            var index = section.IndexOf(goal);
                            if (index != section.Count - 1)
                            {
                                section.Move(index, index + 1);
                                var sectionIndex = Data.IndexOf(section);
                                Data.Remove(section);
                                Data.Insert(sectionIndex, section);
                            }
                            return;
                        }
                    }
                }
            }
            SaveData();

        }
        /// <summary>
        /// Closing all popup menu's.
        /// </summary>
        private void CloseWindows()
        {
            MenuSignedOut.IsVisible = false;
            MenuSignedIn.IsVisible = false;
            AddSectionMenu.IsVisible = false;
            for (int i = 11; i < MainAbsoluteLayout.Children.Count;)
            {
                MainAbsoluteLayout.Children.RemoveAt(i);
            }
        }
        /// <summary>
        /// Displaying add goal popup menu.
        /// </summary>
        /// <param name="label"></param>
        private void AddGoal(Label label)
        {
            CloseWindows();
            ChangingValue = label.Text;
            AddGoalEntryGrid.IsVisible = !AddGoalEntryGrid.IsVisible;
        }
        /// <summary>
        /// Save all data to the local storage.
        /// </summary>
        private void SaveData()
        {
            _savedData = new List<SavedData>();
            var _emptyStorage = new List<SavedData>();
            foreach (var goalPlanItemModel in _viewModel)
            {
                var data = new List<Section>();
                foreach (var section in goalPlanItemModel.Data)
                {
                    data.Add(new Section()
                    {
                        Goals = section.ToList(),
                        SectionName = section.Key.SectionName
                    });
                }
                _savedData.Add(new SavedData()
                {
                    Date = goalPlanItemModel.Date,
                    Data = data,
                });
                _emptyStorage.Add(new SavedData()
                {
                    Date = goalPlanItemModel.Date,
                    Data = new List<Section>()
                });
            }
            Settings.PlanData = JsonConvert.SerializeObject(_savedData);
            Settings.EmptyStorage = JsonConvert.SerializeObject(_emptyStorage);
        }
        /// <summary>
        /// Displaying and updating progress window.
        /// </summary>
        private void ChangeCurrentStatus()
        {
            StatusText.Text = Settings.CurrentAction;
            StatusText.FontSize = _fontSizeHelper.CalculateFontSize(StatusText.Text, _scaledScreenHeight * 0.1,
                _scaledScreenWidth * 0.3);
            StatusLayout.IsVisible = true;
            StatusLayout.FadeTo(1, 250, Easing.Linear);
        }
        /// <summary>
        /// Dynamically adjusting listview height, depending on its rows.
        /// </summary>
        private void AdjustListViewHeight()
        {
            int rows=0;
            rows += Data.Count;
            foreach (var sections in Data)
            {
                rows += sections.Count;
            }

            ListGrid.HeightRequest = rows * 50;
        }

        #endregion
    }
}