using System;
using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using CamboBIM.Revit2024.Addin.Licensing;

namespace CamboBIM.Revit2024.Addin
{
    public class App : IExternalApplication
    {
        // Keep this switch so the main MHNK button can be restored later without deleting code.
        private static readonly bool ShowCamboBimRibbonButton = false;

        private static OnlineLicenseService _licenseService;
        private static bool _licenseValidated;
        private static bool _licensePromptInProgress;
        private static object _camboBimRibbonTab;
        private static PropertyChangedEventHandler _ribbonTabPropertyChangedHandler;
        private static string _autodeskLoginUserId = "";

        public Result OnStartup(UIControlledApplication application)
        {
            _licenseService = new OnlineLicenseService();

            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            const string camboBimTabName = "MHNK";
            MhnkLogger.Info("MHNK startup. Assembly: " + assemblyPath);

            CreateTabWithTools(application, camboBimTabName, assemblyPath, "MHNK");
            TryAttachRibbonTabActivationHandler(camboBimTabName);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            MhnkLogger.Info("MHNK shutdown.");
            DetachRibbonTabActivationHandler();
            // Keep cached seat/session so users are not prompted to log in again next launch.
            _licenseService?.ReleaseOnShutdown(releaseSeat: false);

            return Result.Succeeded;
        }

        internal static void SetAutodeskLoginUserId(string autodeskLoginUserId)
        {
            if (!string.IsNullOrWhiteSpace(autodeskLoginUserId))
            {
                _autodeskLoginUserId = autodeskLoginUserId.Trim();
            }
        }

        internal static bool EnsureLicenseActivated(out string failureMessage, string autodeskLoginUserId = "", bool forceFreshLogin = false)
        {
            failureMessage = "";

            if (_licenseValidated && !forceFreshLogin)
            {
                return true;
            }

            SetAutodeskLoginUserId(autodeskLoginUserId);

            _licenseService ??= new OnlineLicenseService();

            if (!_licenseService.ValidateOrLogin(_autodeskLoginUserId, out failureMessage, forceFreshLogin))
            {
                return false;
            }

            _licenseValidated = true;
            return true;
        }

        internal static bool TryRefreshLicenseStatus(out OnlineLicenseSession session, out string failureMessage)
        {
            _licenseService ??= new OnlineLicenseService();

            bool ok = _licenseService.RefreshSessionStatus(out failureMessage);
            session = _licenseService.GetCurrentSessionSnapshot();
            return ok;
        }

        internal static OnlineLicenseSession GetLicenseSessionSnapshot()
        {
            _licenseService ??= new OnlineLicenseService();

            return _licenseService.GetCurrentSessionSnapshot();
        }

        internal static bool ChangeLicensePassword(string username, string currentPassword, string newPassword, out string failureMessage)
        {
            _licenseService ??= new OnlineLicenseService();

            return _licenseService.ChangePassword(username, currentPassword, newPassword, out failureMessage);
        }

        internal static string GetLicenseSupportContactText()
        {
            _licenseService ??= new OnlineLicenseService();

            return _licenseService.BuildSupportContactText();
        }

        internal static string GetLicenseAccountRequestText()
        {
            _licenseService ??= new OnlineLicenseService();

            return _licenseService.BuildAccountRequestText();
        }

        internal static bool IsLicenseTestModeUnlocked()
        {
            _licenseService ??= new OnlineLicenseService();

            return _licenseService.IsUnlockedTestMode();
        }

        private static void TryAttachRibbonTabActivationHandler(string tabName)
        {
            try
            {
                Type componentManagerType = Type.GetType("Autodesk.Windows.ComponentManager, AdWindows", false);
                if (componentManagerType == null)
                {
                    return;
                }

                PropertyInfo ribbonProperty = componentManagerType.GetProperty("Ribbon", BindingFlags.Public | BindingFlags.Static);
                object ribbon = ribbonProperty?.GetValue(null, null);
                if (ribbon == null)
                {
                    return;
                }

                PropertyInfo tabsProperty = ribbon.GetType().GetProperty("Tabs", BindingFlags.Public | BindingFlags.Instance);
                IEnumerable tabs = tabsProperty != null ? tabsProperty.GetValue(ribbon, null) as IEnumerable : null;
                if (tabs == null)
                {
                    return;
                }

                foreach (object tab in tabs)
                {
                    if (!IsMatchingRibbonTab(tab, tabName))
                    {
                        continue;
                    }

                    EventInfo propertyChangedEvent = tab.GetType().GetEvent("PropertyChanged", BindingFlags.Public | BindingFlags.Instance);
                    if (propertyChangedEvent == null)
                    {
                        return;
                    }

                    _ribbonTabPropertyChangedHandler = OnRibbonTabPropertyChanged;
                    propertyChangedEvent.AddEventHandler(tab, _ribbonTabPropertyChangedHandler);
                    _camboBimRibbonTab = tab;
                    return;
                }
            }
            catch
            {
            }
        }

        private static void DetachRibbonTabActivationHandler()
        {
            if (_camboBimRibbonTab == null || _ribbonTabPropertyChangedHandler == null)
            {
                return;
            }

            try
            {
                EventInfo propertyChangedEvent = _camboBimRibbonTab.GetType().GetEvent("PropertyChanged", BindingFlags.Public | BindingFlags.Instance);
                propertyChangedEvent?.RemoveEventHandler(_camboBimRibbonTab, _ribbonTabPropertyChangedHandler);
            }
            catch
            {
            }

            _camboBimRibbonTab = null;
            _ribbonTabPropertyChangedHandler = null;
        }

        private static bool IsMatchingRibbonTab(object tab, string tabName)
        {
            if (tab == null || string.IsNullOrWhiteSpace(tabName))
            {
                return false;
            }

            try
            {
                PropertyInfo titleProperty = tab.GetType().GetProperty("Title", BindingFlags.Public | BindingFlags.Instance);
                string title = titleProperty != null ? Convert.ToString(titleProperty.GetValue(tab, null)) : "";
                if (string.Equals(title, tabName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                PropertyInfo idProperty = tab.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
                string id = idProperty != null ? Convert.ToString(idProperty.GetValue(tab, null)) : "";
                return !string.IsNullOrWhiteSpace(id) &&
                       id.Contains(tabName, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void OnRibbonTabPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_licenseValidated || _licensePromptInProgress)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_autodeskLoginUserId))
            {
                // Autodesk LoginUserId is captured from command context.
                // Skip eager prompt until we have a known Autodesk account.
                return;
            }

            if (e != null &&
                !string.IsNullOrWhiteSpace(e.PropertyName) &&
                !string.Equals(e.PropertyName, "IsActive", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            bool isActive = false;
            try
            {
                PropertyInfo isActiveProperty = sender?.GetType().GetProperty("IsActive", BindingFlags.Public | BindingFlags.Instance);
                if (isActiveProperty != null)
                {
                    object activeValue = isActiveProperty.GetValue(sender, null);
                    isActive = activeValue is bool value && value;
                }
            }
            catch
            {
                return;
            }

            if (!isActive)
            {
                return;
            }

            _licensePromptInProgress = true;
            try
            {
                if (!EnsureLicenseActivated(out string licenseFailure))
                {
                    TaskDialog.Show("MHNK License", licenseFailure);
                }
            }
            finally
            {
                _licensePromptInProgress = false;
            }
        }

        private static void CreateTabWithTools(
            UIControlledApplication application,
            string tabName,
            string assemblyPath,
            string buttonIdPrefix)
        {
            // Create Tab (ignore if already exists)
            try { application.CreateRibbonTab(tabName); }
            catch { /* tab already exists */ }

            RibbonPanel architecturePanel = GetOrCreatePanel(application, tabName, "ARC");
            RibbonPanel structuralPanel = GetOrCreatePanel(application, tabName, "STR");
            RibbonPanel accountPanel = GetOrCreatePanel(application, tabName, "Account");
            AddArchitectureButtons(architecturePanel, assemblyPath, buttonIdPrefix);
            AddStructuralButtons(structuralPanel, assemblyPath, buttonIdPrefix);
            AddAccountButtons(accountPanel, assemblyPath, buttonIdPrefix);
        }

        private static void AddArchitectureButtons(RibbonPanel architecturePanel, string assemblyPath, string buttonIdPrefix)
        {
            PushButtonData filterButton = CreateDisciplineButton(
                buttonIdPrefix,
                "Arc",
                "Filter",
                typeof(OpenMhnkFilterCommand),
                assemblyPath,
                "Select, isolate, and review model elements with MHNK filter presets.",
                "images/MHNK_ARCH_FILTER.png");

            PushButtonData creationButton = CreateDisciplineButton(
                buttonIdPrefix,
                "Arc",
                "Creation",
                typeof(OpenMhnkCreationCommand),
                assemblyPath,
                "Create architectural model elements from CAD, model lines, and MHNK presets.",
                "images/MHNK_ARCH_CREATION.png");

            PushButtonData editionButton = CreateDisciplineButton(
                buttonIdPrefix,
                "Arc",
                "Edition",
                typeof(OpenMhnkEditionCommand),
                assemblyPath,
                "Edit, align, join, split, and update model elements in batches.",
                "images/MHNK_ARCH_EDITION.png");

            PushButtonData solidsButton = CreateDisciplineButton(
                buttonIdPrefix,
                "Arc",
                "Solids",
                typeof(OpenMhnkSolidsCommand),
                assemblyPath,
                "Create and review solids, direct shapes, and volume-oriented model data.",
                "images/MHNK_ARCH_SOLIDS.png");

            PushButtonData xpressButton = CreateDisciplineButton(
                buttonIdPrefix,
                "Arc",
                "Xpress",
                typeof(OpenMhnkXpressCommand),
                assemblyPath,
                "Run quick MHNK automation presets for common Revit production tasks.",
                "images/MHNK_ARCH_XPRESS.png");

            PulldownButtonData arcPulldownData = new(
                $"{buttonIdPrefix}_ArcTools",
                "ARC\nTOOLS"
            );

            if (architecturePanel.AddItem(arcPulldownData) is PulldownButton arcPulldown)
            {
                arcPulldown.ToolTip = "ARC tools: Filter, Creation, Edition, Solids, and Xpress.";
                arcPulldown.LargeImage = LoadRibbonIcon("images/MHNK_DISC_ARC.png", 32);
                arcPulldown.Image = LoadRibbonIcon("images/MHNK_DISC_ARC.png", 16);
                arcPulldown.AddPushButton(filterButton);
                arcPulldown.AddPushButton(creationButton);
                arcPulldown.AddPushButton(editionButton);
                arcPulldown.AddPushButton(solidsButton);
                arcPulldown.AddPushButton(xpressButton);
            }
            else
            {
                architecturePanel.AddItem(filterButton);
                architecturePanel.AddItem(creationButton);
                architecturePanel.AddItem(editionButton);
                architecturePanel.AddItem(solidsButton);
                architecturePanel.AddItem(xpressButton);
            }
        }

        private static PushButtonData CreateDisciplineButton(
            string buttonIdPrefix,
            string discipline,
            string label,
            Type commandType,
            string assemblyPath,
            string tooltip,
            string iconPath)
        {
            PushButtonData button = new(
                $"{buttonIdPrefix}_{discipline}_{label}",
                label,
                assemblyPath,
                CamboBimRuntime.GetCommandClassName(commandType))
            {
                ToolTip = tooltip,
                LargeImage = LoadRibbonIcon(iconPath, 32),
                Image = LoadRibbonIcon(iconPath, 16)
            };

            return button;
        }

        private static void AddStructuralButtons(RibbonPanel toolsPanel, string assemblyPath, string buttonIdPrefix)
        {
            PushButtonData cad2ModelButton = new(
                $"{buttonIdPrefix}_OpenCad2Model",
                "CAD2MODEL",
                assemblyPath,
                CamboBimRuntime.GetCommandClassName(typeof(OpenCad2ModelCommand))
            )
            {
                ToolTip = "Open MHNK Identify tools.",
                LargeImage = LoadRibbonIcon("images/identify-tools/auto-identify.svg", 32),
                Image = LoadRibbonIcon("images/identify-tools/auto-identify.svg", 16)
            };

            PushButtonData qsToolButton = new(
                $"{buttonIdPrefix}_OpenQsTool",
                "QS TOOLS",
                assemblyPath,
                CamboBimRuntime.GetCommandClassName(typeof(OpenQsToolCommand))
            )
            {
                ToolTip = "Open MHNK and jump to QS TOOLS.",
                LargeImage = LoadRibbonIcon("images/TAB_QS_TOOLS.png", 32),
                Image = LoadRibbonIcon("images/TAB_QS_TOOLS.png", 16)
            };

            PushButtonData linksheetButton = new(
                $"{buttonIdPrefix}_OpenLinksheet",
                "LINKSHEET",
                assemblyPath,
                CamboBimRuntime.GetCommandClassName(typeof(OpenLinksheetCommand))
            )
            {
                ToolTip = "Open MHNK and jump to LINKSHEET.",
                LargeImage = LoadRibbonIcon("images/TAB_SHEETLINK.png", 32),
                Image = LoadRibbonIcon("images/TAB_SHEETLINK.png", 16)
            };

            PushButtonData autoJoinButton = new(
                $"{buttonIdPrefix}_OpenAutoJoin",
                "AUTO JOIN",
                assemblyPath,
                CamboBimRuntime.GetCommandClassName(typeof(OpenAutoJoinCommand))
            )
            {
                ToolTip = "Open MHNK and jump to AUTO JOIN.",
                LargeImage = LoadRibbonIcon("images/TAB_AUTO JOIN.png", 32),
                Image = LoadRibbonIcon("images/TAB_AUTO JOIN.png", 16)
            };

            PushButtonData siteProgressButton = new(
                $"{buttonIdPrefix}_OpenSiteProgress",
                "SITE PROGRESS",
                assemblyPath,
                CamboBimRuntime.GetCommandClassName(typeof(OpenSiteProgressCommand))
            )
            {
                ToolTip = "Open MHNK and jump to SITE PROGRESS.",
                LargeImage = LoadRibbonIcon("images/TAB_SITE PROGRESS.png", 32),
                Image = LoadRibbonIcon("images/TAB_SITE PROGRESS.png", 16)
            };

            PushButtonData drawingButton = new(
                $"{buttonIdPrefix}_OpenDrawing",
                "REINFORCEMENT",
                assemblyPath,
                CamboBimRuntime.GetCommandClassName(typeof(OpenDrawingCommand))
            )
            {
                ToolTip = "Open MHNK and jump to REINFORCEMENT.",
                LargeImage = LoadRibbonIcon("images/TAB_DRAWING.png", 32),
                Image = LoadRibbonIcon("images/TAB_DRAWING.png", 16)
            };

            PushButtonData drawingPtButton = new(
                $"{buttonIdPrefix}_OpenDrawingPt",
                "DRAWING\nPT",
                assemblyPath,
                CamboBimRuntime.GetCommandClassName(typeof(OpenDrawingPtCommand))
            )
            {
                ToolTip = "Import ADAPT-Builder PT drawings or profile tables into Revit.",
                LongDescription = "Opens the MHNK CAD2MODEL workspace and starts the ADAPT/PT import workflow for .adm, .dwg, .dxf, CSV, and Excel exports.",
                LargeImage = LoadRibbonIcon("images/identify-tools/post-cast-strip.svg", 32),
                Image = LoadRibbonIcon("images/identify-tools/post-cast-strip.svg", 16)
            };

            PushButtonData sCurveButton = new(
                $"{buttonIdPrefix}_OpenSCurve",
                "BI TOOLS",
                assemblyPath,
                CamboBimRuntime.GetCommandClassName(typeof(OpenSCurveCommand))
            )
            {
                ToolTip = "Open MHNK and jump to BI TOOLS.",
                LargeImage = LoadRibbonIcon("images/TAB_SCURVE.png", 32),
                Image = LoadRibbonIcon("images/TAB_SCURVE.png", 16)
            };

            PushButtonData pmDashboardButton = new(
                $"{buttonIdPrefix}_OpenPmDashboard",
                "PM TOOLS",
                assemblyPath,
                CamboBimRuntime.GetCommandClassName(typeof(OpenPmDashboardCommand))
            )
            {
                ToolTip = "Open MHNK and jump to PM TOOLS.",
                LargeImage = LoadRibbonIcon("images/TAB_PM_DASHBOARD.png", 32),
                Image = LoadRibbonIcon("images/TAB_PM_DASHBOARD.png", 16)
            };

            PushButtonData sharedParamCheckButton = new(
                $"{buttonIdPrefix}_DetectSharedParameters",
                "SP CHECK",
                assemblyPath,
                CamboBimRuntime.GetCommandClassName(typeof(DetectSharedParametersCommand))
            )
            {
                ToolTip = "Detect shared parameters in the active document and show missing key fields.",
                LongDescription = "Generate and validate BOREY shared parameters in the active model.",
                LargeImage = LoadRibbonIcon("images/TAB_SP_CHECK.png", 32),
                Image = LoadRibbonIcon("images/TAB_SP_CHECK.png", 16)
            };

            // Ungrouped layout: each tool appears as its own ribbon button.
            toolsPanel.AddItem(cad2ModelButton);
            toolsPanel.AddItem(qsToolButton);
            toolsPanel.AddItem(linksheetButton);
            toolsPanel.AddItem(autoJoinButton);
            toolsPanel.AddItem(drawingButton);
            toolsPanel.AddItem(drawingPtButton);

            PushButtonData camboBimWindow = new(
                $"{buttonIdPrefix}_OpenWindow",
                "MHNK",
                assemblyPath,
                CamboBimRuntime.GetCommandClassName(typeof(CamboBIMWindowCommand))
            )
            {
                ToolTip = "Open the MHNK modeless window.",
                LongDescription = "Shows a modeless WPF window so you can keep working in Revit.",
                LargeImage = LoadImageFromResource("images/MHNK_32.png"),
                Image = LoadImageFromResource("images/MHNK_16.png")
            };

            PushButtonData boreyWindow = new(
                $"{buttonIdPrefix}_OpenBorey",
                "BOREY",
                assemblyPath,
                CamboBimRuntime.GetCommandClassName(typeof(OpenBoreyCommand))
            )
            {
                ToolTip = "Open MHNK and jump to BOREY.",
                LongDescription = "Opens the MHNK modeless window focused on BOREY tools.",
                LargeImage = LoadRibbonIcon("images/TAB_BOREY.png", 32),
                Image = LoadRibbonIcon("images/TAB_BOREY.png", 16)
            };

            toolsPanel.AddItem(siteProgressButton);
            toolsPanel.AddItem(sCurveButton);
            toolsPanel.AddItem(pmDashboardButton);

            PulldownButtonData boreyPulldownData = new(
                $"{buttonIdPrefix}_BoreyTools",
                "BOREY"
            );
            if (toolsPanel.AddItem(boreyPulldownData) is PulldownButton boreyPulldown)
            {
                boreyPulldown.ToolTip = "BOREY tools and shared parameter checks.";
                boreyPulldown.LargeImage = LoadRibbonIcon("images/TAB_BOREY.png", 32);
                boreyPulldown.Image = LoadRibbonIcon("images/TAB_BOREY.png", 16);
                boreyPulldown.AddPushButton(boreyWindow);
                boreyPulldown.AddPushButton(sharedParamCheckButton);
            }
            else
            {
                // Fallback: still provide direct buttons if pulldown is unavailable.
                toolsPanel.AddItem(boreyWindow);
                toolsPanel.AddItem(sharedParamCheckButton);
            }

            // Keep the main MHNK window button hidden unless explicitly enabled.
            if (ShowCamboBimRibbonButton)
            {
                toolsPanel.AddItem(camboBimWindow);
            }
        }

        private static void AddAccountButtons(RibbonPanel accountPanel, string assemblyPath, string buttonIdPrefix)
        {
            PushButtonData aboutMeButton = new(
                $"{buttonIdPrefix}_AboutMe",
                "ABOUT\nME",
                assemblyPath,
                CamboBimRuntime.GetCommandClassName(typeof(OpenAboutMeCommand))
            )
            {
                ToolTip = "Show MHNK information and support contacts.",
                LongDescription = "Opens About Me information for MHNK users.",
                LargeImage = LoadImageFromResource("images/MHNK_32.png"),
                Image = LoadImageFromResource("images/MHNK_16.png")
            };

            PushButtonData licenseLoginButton = new(
                $"{buttonIdPrefix}_LicenseLogin",
                "LICENSE\nLOGIN",
                assemblyPath,
                CamboBimRuntime.GetCommandClassName(typeof(OpenLicenseLoginCommand))
            )
            {
                ToolTip = "Sign in or refresh MHNK online license.",
                LongDescription = "Opens the MHNK online license login dialog from the ribbon.",
                LargeImage = LoadImageFromResource("images/MHNK_32.png"),
                Image = LoadImageFromResource("images/MHNK_16.png")
            };

            PushButtonData userGuideButton = new(
                $"{buttonIdPrefix}_UserGuide",
                "USER\nGUIDE",
                assemblyPath,
                CamboBimRuntime.GetCommandClassName(typeof(OpenUserGuideCommand))
            )
            {
                ToolTip = "Open full MHNK user guideline with images and setup steps.",
                LongDescription = "Shows an HTML user guideline window, including tool descriptions, license setup, and troubleshooting.",
                LargeImage = LoadImageFromResource("images/MHNK_32.png"),
                Image = LoadImageFromResource("images/MHNK_16.png")
            };

            PushButtonData diagnosticsButton = new(
                $"{buttonIdPrefix}_Diagnostics",
                "DIAG\nNOSTICS",
                assemblyPath,
                CamboBimRuntime.GetCommandClassName(typeof(OpenDiagnosticsCommand))
            )
            {
                ToolTip = "Show MHNK diagnostics for support and deployment checks.",
                LongDescription = "Shows Revit version, loaded DLL path, manifest path, license config, and log location.",
                LargeImage = LoadImageFromResource("images/MHNK_32.png"),
                Image = LoadImageFromResource("images/MHNK_16.png")
            };

            PulldownButtonData accountPulldownData = new(
                $"{buttonIdPrefix}_AccountMenu",
                "ACCOUNT"
            );

            if (accountPanel.AddItem(accountPulldownData) is PulldownButton accountPulldown)
            {
                accountPulldown.ToolTip = "Account tools: About Me, License Login, User Guide, and Diagnostics.";
                accountPulldown.LargeImage = LoadImageFromResource("images/MHNK_32.png");
                accountPulldown.Image = LoadImageFromResource("images/MHNK_16.png");
                accountPulldown.AddPushButton(aboutMeButton);
                accountPulldown.AddPushButton(licenseLoginButton);
                accountPulldown.AddPushButton(userGuideButton);
                accountPulldown.AddPushButton(diagnosticsButton);
            }
            else
            {
                accountPanel.AddItem(aboutMeButton);
                accountPanel.AddItem(licenseLoginButton);
                accountPanel.AddItem(userGuideButton);
                accountPanel.AddItem(diagnosticsButton);
            }
        }

        private static RibbonPanel GetOrCreatePanel(UIControlledApplication application, string tabName, string panelName)
        {
            foreach (RibbonPanel panel in application.GetRibbonPanels(tabName))
            {
                if (panel.Name == panelName)
                    return panel;
            }

            return application.CreateRibbonPanel(tabName, panelName);
        }

        private static BitmapImage LoadImageFromResource(string relativeUri)
        {
            Uri uri = new(CamboBimRuntime.GetPackUri(relativeUri), UriKind.Absolute);

            BitmapImage image = new();
            image.BeginInit();
            image.UriSource = uri;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            image.EndInit();
            image.Freeze();
            return image;
        }

        private static ImageSource LoadRibbonIcon(string relativeUri, int iconSize)
        {
            try
            {
                if (relativeUri != null &&
                    relativeUri.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    return RenderSvgRibbonIcon(relativeUri, iconSize);
                }

                BitmapSource source = LoadImageFromResource(relativeUri);
                BitmapSource cropped = CropNearWhiteMargin(source);
                BitmapSource square = CenterCropSquare(cropped);
                return FitToSquare(square, iconSize);
            }
            catch
            {
                return LoadImageFromResource("images/MHNK_32.png");
            }
        }

        private static ImageSource RenderSvgRibbonIcon(string relativeUri, int iconSize)
        {
            string iconFile = Path.GetFileName(relativeUri ?? "");
            FrameworkElement icon = IdentifySvgIconFactory.CreateIcon(iconFile, iconSize);
            icon.Width = iconSize;
            icon.Height = iconSize;
            icon.Measure(new Size(iconSize, iconSize));
            icon.Arrange(new Rect(0, 0, iconSize, iconSize));
            icon.UpdateLayout();

            RenderTargetBitmap output = new(iconSize, iconSize, 96, 96, PixelFormats.Pbgra32);
            output.Render(icon);
            output.Freeze();
            return output;
        }

        private static BitmapSource CropNearWhiteMargin(BitmapSource source)
        {
            if (source == null || source.PixelWidth < 2 || source.PixelHeight < 2)
            {
                return source;
            }

            BitmapSource bgra;
            if (source.Format == PixelFormats.Bgra32)
            {
                bgra = source;
            }
            else
            {
                bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            }

            int width = bgra.PixelWidth;
            int height = bgra.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            bgra.CopyPixels(pixels, stride, 0);

            int minX = width;
            int minY = height;
            int maxX = -1;
            int maxY = -1;

            // Estimate background from corners so icons with non-white canvas are also cropped correctly.
            Color bg = EstimateCornerBackground(pixels, stride, width, height);

            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int i = row + (x * 4);
                    byte b = pixels[i + 0];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];
                    byte a = pixels[i + 3];

                    bool nearWhite = r > 245 && g > 245 && b > 245;
                    bool nearBackground = Math.Abs(r - bg.R) <= 16
                                          && Math.Abs(g - bg.G) <= 16
                                          && Math.Abs(b - bg.B) <= 16;
                    if (a > 10 && !nearWhite && !nearBackground)
                    {
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (maxX < minX || maxY < minY)
            {
                return source;
            }

            int cropWidth = maxX - minX + 1;
            int cropHeight = maxY - minY + 1;
            if (cropWidth < 8 || cropHeight < 8)
            {
                return source;
            }

            int pad = 1;
            int x0 = Math.Max(0, minX - pad);
            int y0 = Math.Max(0, minY - pad);
            int x1 = Math.Min(width - 1, maxX + pad);
            int y1 = Math.Min(height - 1, maxY + pad);

            Int32Rect rect = new(x0, y0, (x1 - x0 + 1), (y1 - y0 + 1));
            CroppedBitmap cropped = new(bgra, rect);
            cropped.Freeze();
            return cropped;
        }

        private static Color EstimateCornerBackground(byte[] pixels, int stride, int width, int height)
        {
            int[,] points = new int[,]
            {
                { 0, 0 },
                { Math.Max(0, width - 1), 0 },
                { 0, Math.Max(0, height - 1) },
                { Math.Max(0, width - 1), Math.Max(0, height - 1) },
                { Math.Max(0, width / 2), 0 },
                { Math.Max(0, width / 2), Math.Max(0, height - 1) }
            };

            int count = points.GetLength(0);
            int sumR = 0;
            int sumG = 0;
            int sumB = 0;

            for (int i = 0; i < count; i++)
            {
                int x = points[i, 0];
                int y = points[i, 1];
                int idx = (y * stride) + (x * 4);
                sumB += pixels[idx + 0];
                sumG += pixels[idx + 1];
                sumR += pixels[idx + 2];
            }

            return Color.FromRgb(
                (byte)(sumR / count),
                (byte)(sumG / count),
                (byte)(sumB / count));
        }

        private static RenderTargetBitmap FitToSquare(BitmapSource source, int size)
        {
            if (source == null)
            {
                return null;
            }

            double scale = Math.Min((double)size / source.PixelWidth, (double)size / source.PixelHeight);
            double drawWidth = source.PixelWidth * scale;
            double drawHeight = source.PixelHeight * scale;
            double offsetX = (size - drawWidth) * 0.5;
            double offsetY = (size - drawHeight) * 0.5;

            DrawingVisual visual = new();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, size, size));
                dc.DrawImage(source, new Rect(offsetX, offsetY, drawWidth, drawHeight));
            }

            RenderTargetBitmap output = new(size, size, 96, 96, PixelFormats.Pbgra32);
            output.Render(visual);
            output.Freeze();
            return output;
        }

        private static BitmapSource CenterCropSquare(BitmapSource source)
        {
            if (source == null)
            {
                return null;
            }

            int width = source.PixelWidth;
            int height = source.PixelHeight;
            if (width <= 0 || height <= 0)
            {
                return source;
            }

            int side = Math.Min(width, height);
            if (side <= 0)
            {
                return source;
            }

            // Keep full image if it is already near-square.
            double ratio = (double)width / height;
            if (ratio > 0.9 && ratio < 1.1)
            {
                return source;
            }

            int x = (width - side) / 2;
            int y = (height - side) / 2;
            Int32Rect rect = new(x, y, side, side);
            CroppedBitmap cropped = new(source, rect);
            cropped.Freeze();
            return cropped;
        }
    }
}
