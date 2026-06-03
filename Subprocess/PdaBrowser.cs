using System;

using System.Drawing;

using System.Drawing.Drawing2D;

using System.Runtime.InteropServices;

using System.Threading.Tasks;

using System.Windows.Forms;

using Microsoft.Web.WebView2.Core;

using Microsoft.Web.WebView2.WinForms;



namespace TikTokPda

{

    public class PdaBrowser : Form

    {

        [DllImport("user32.dll")]

        public static extern bool ReleaseCapture();

        

        [DllImport("user32.dll")]

        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);



        [DllImport("user32.dll")]

        public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);



        [DllImport("user32.dll")]

        [return: MarshalAs(UnmanagedType.Bool)]

        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);



        [StructLayout(LayoutKind.Sequential)]

        public struct RECT

        {

            public int Left;

            public int Top;

            public int Right;

            public int Bottom;

        }



        private WebView2 webView;

        private Timer ledTimer;

        private bool ledState = false;

        private PdaButton[] buttons;

        private Point mousePos;

        private bool isLoading = true;

        private IntPtr parentHandle = IntPtr.Zero;

        private bool shouldBlockUnload = false;



        private const string InitialUrl = "https://www.tiktok.com/";

        private const string MobileUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";



        public PdaBrowser() : this(IntPtr.Zero) { }



        public PdaBrowser(IntPtr parent)

        {

            Console.WriteLine("[PDA] Constructor started.");

            this.parentHandle = parent;

            this.FormBorderStyle = FormBorderStyle.None;

            this.Size = new Size(540, 880);

            this.StartPosition = FormStartPosition.CenterScreen;

            this.BackColor = Color.FromArgb(24, 26, 30);

            this.DoubleBuffered = true;

            this.Text = "Europan PDA - TikTok";



            // Make the form window region slightly rounded for aesthetics

            GraphicsPath path = new GraphicsPath();

            int r = 30; // Radius

            path.AddArc(0, 0, r, r, 180, 90);

            path.AddArc(Width - r, 0, r, r, 270, 90);

            path.AddArc(Width - r, Height - r, r, r, 0, 90);

            path.AddArc(0, Height - r, r, r, 90, 90);

            path.CloseAllFigures();

            this.Region = new Region(path);



            InitializePdaButtons();

            InitializeWebView();

            InitializeLedTimer();

        }



        protected override void OnLoad(EventArgs e)

        {

            base.OnLoad(e);

            if (parentHandle != IntPtr.Zero)

            {

                try

                {

                    Console.WriteLine("[PDA] Setting parent window to: " + parentHandle);

                    SetParent(this.Handle, parentHandle);

                    

                    RECT rect;

                    if (GetWindowRect(parentHandle, out rect))

                    {

                        int parentWidth = rect.Right - rect.Left;

                        int parentHeight = rect.Bottom - rect.Top;

                        this.Location = new Point((parentWidth - this.Width) / 2, (parentHeight - this.Height) / 2);

                        Console.WriteLine("[PDA] Embedded and centered inside parent window. Position: " + this.Location);

                    }

                }

                catch (Exception ex)

                {

                    Console.WriteLine("[PDA Error] Failed to set parent window: " + ex.ToString());

                }

            }

        }



        private void InitializePdaButtons()

        {

            // Bottom buttons (Y coordinates 785 - 835)

            buttons = new PdaButton[]

            {

                new PdaButton

                {

                    Bounds = new Rectangle(30, 790, 75, 45),

                    Label = "BACK",

                    BorderColor = Color.FromArgb(80, 85, 95),

                    GlowColor = Color.FromArgb(120, 130, 150),

                    Action = () => { if (webView != null && webView.CanGoBack) webView.GoBack(); }

                },

                new PdaButton

                {

                    Bounds = new Rectangle(120, 785, 85, 55), // Home is slightly larger

                    Label = "HOME",

                    BorderColor = Color.FromArgb(0, 180, 160),

                    GlowColor = Color.FromArgb(0, 255, 220),

                    Action = () => { if (webView != null) webView.Source = new Uri(InitialUrl); }

                },

                new PdaButton

                {

                    Bounds = new Rectangle(220, 790, 85, 45),

                    Label = "LOGIN",

                    BorderColor = Color.FromArgb(200, 160, 0),

                    GlowColor = Color.FromArgb(255, 210, 0),

                    Action = () => { if (webView != null) webView.Source = new Uri("https://www.tiktok.com/login"); }

                },

                new PdaButton

                {

                    Bounds = new Rectangle(320, 790, 85, 45),

                    Label = "RELOAD",

                    BorderColor = Color.FromArgb(80, 85, 95),

                    GlowColor = Color.FromArgb(120, 130, 150),

                    Action = () => { if (webView != null) webView.Reload(); }

                },

                new PdaButton

                {

                    Bounds = new Rectangle(420, 790, 90, 45),

                    Label = "SHUTDOWN",

                    BorderColor = Color.FromArgb(180, 40, 40),

                    GlowColor = Color.FromArgb(255, 50, 50),

                    Action = () => { Application.Exit(); }

                }

            };

        }



        private static void Log(string message)

        {

            try

            {

                string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pda_browser.log");

                System.IO.File.AppendAllText(logPath, string.Format("[{0}] {1}\r\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), message));

                Console.WriteLine(message);

            }

            catch { }

        }



        private async void InitializeWebView()

        {

            Log("[PDA] InitializeWebView started.");

            try

            {

                webView = new WebView2

                {

                    Location = new Point(25, 65),

                    Size = new Size(490, 700),

                    BackColor = Color.Black

                };



                this.Controls.Add(webView);



                // Set up environment with a local user data folder to avoid permission errors

                string localAppDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebViewData");

                Log("[PDA] Creating WebView2 environment in " + localAppDir);

                var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");

                var env = await CoreWebView2Environment.CreateAsync(null, localAppDir, options);

                Log("[PDA] WebView2 environment created. Initializing control...");

                await webView.EnsureCoreWebView2Async(env);

                Log("[PDA] WebView2 control initialized successfully.");



                // Set user agent to a desktop agent

                webView.CoreWebView2.Settings.UserAgent = MobileUserAgent;

                webView.CoreWebView2.Settings.IsZoomControlEnabled = false;

                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;



                // Use native layout size (490x700) to prevent Chromium scaling/decoding issues

                Log("[PDA] Native layout size active.");



                // Handle process failures

                webView.CoreWebView2.ProcessFailed += (s, e) =>

                {

                    Log(string.Format("[PROCESS FAILED] Kind: {0}", e.ProcessFailedKind));

                };



                // Handle web messages (for javascript logging)

                webView.WebMessageReceived += (s, e) =>

                {

                    try

                    {

                        string json = e.WebMessageAsJson;

                        Log(string.Format("[JS MESSAGE] {0}", json));

                    }

                    catch (Exception ex)

                    {

                        Log(string.Format("[JS MESSAGE ERROR] {0}", ex.Message));

                    }

                };



                // Intercept Beforeunload dialogs to block redirects without white screen

                webView.CoreWebView2.ScriptDialogOpening += (s, e) =>

                {

                    try

                    {

                        Log(string.Format("[PDA] Script dialog opening: {0} (Message: {1})", e.Kind, e.Message));

                        if (e.Kind == CoreWebView2ScriptDialogKind.Beforeunload)

                        {

                            if (shouldBlockUnload)

                            {

                                // To block the navigation and stay on the current page, we DO NOT call e.Accept()

                                shouldBlockUnload = false;

                                isLoading = false;

                                this.Invalidate(new Rectangle(430, 10, 100, 50));

                                Log("[PDA] Programmatically cancelled Beforeunload dialog to block redirect.");

                            }

                            else

                            {

                                e.Accept(); // Call Accept() to allow normal navigation

                                Log("[PDA] Allowed Beforeunload dialog for navigation.");

                            }

                        }

                    }

                    catch (Exception ex)

                    {

                        Log("[PDA Error] ScriptDialogOpening handler failed: " + ex.ToString());

                    }

                };



                // Handle new window requests (like Google/Apple/Facebook login popups)

                webView.CoreWebView2.NewWindowRequested += async (s, e) =>

                {

                    var deferral = e.GetDeferral();

                    

                    string uri = e.Uri.ToString();

                    Log("[PDA] New window requested to: " + uri);

                    

                    string lowerUri = uri.ToLower();

                    bool isAppStoreOrDownload = lowerUri.Contains("apps.apple.com") || 

                                                lowerUri.Contains("play.google.com") || 

                                                lowerUri.Contains("itunes.apple.com") || 

                                                lowerUri.Contains("onelink.me") || 

                                                lowerUri.Contains("tiktok.com/download") ||

                                                lowerUri.Contains("apple.co") ||

                                                lowerUri.StartsWith("itms-apps") ||

                                                lowerUri.Contains("market://");

                                                

                    if (isAppStoreOrDownload)

                    {

                        Log("[PDA] Blocked app download redirection.");

                        e.Handled = true;

                        deferral.Complete();

                        return;

                    }



                    // For login OAuth (Google, Facebook, Apple, etc.) or TikTok popups

                    Log("[PDA] Creating popup window for OAuth: " + uri);

                    

                    Form popupForm = new Form

                    {

                        Size = new Size(540, 700),

                        Text = "OAuth Login - Europa-OS",

                        StartPosition = FormStartPosition.CenterParent,

                        FormBorderStyle = FormBorderStyle.SizableToolWindow

                    };

                    

                    WebView2 popupWebView = new WebView2

                    {

                        Dock = DockStyle.Fill

                    };

                    popupForm.Controls.Add(popupWebView);

                    

                    // Disable the main PDA form to make this behave like a modal dialog, and re-enable on close

                    this.Enabled = false;

                    popupForm.FormClosed += (sender, args) =>

                    {

                        this.Enabled = true;

                    };



                    // Set up event handlers to log navigation and errors in the popup WebView2

                    popupWebView.NavigationStarting += (sender, args) =>

                    {

                        Log("[PDA Popup] Navigation starting: " + args.Uri);

                    };

                    popupWebView.NavigationCompleted += (sender, args) =>

                    {

                        Log("[PDA Popup] Navigation completed (Success: " + args.IsSuccess + ", Error: " + args.WebErrorStatus + ")");

                    };



                    try

                    {

                        // Show the form modelessly first so handles are created and it paints

                        popupForm.Show(this);

                        

                        // We must initialize the popup WebView2 in the same environment so that cookies/session are shared!

                        await popupWebView.EnsureCoreWebView2Async(env);



                        // Set popup user agent to match the main desktop one

                        popupWebView.CoreWebView2.Settings.UserAgent = MobileUserAgent;

                        popupWebView.CoreWebView2.Settings.IsZoomControlEnabled = false;

                        popupWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;



                        // Once initialized, assign e.NewWindow and complete the deferral!

                        e.NewWindow = popupWebView.CoreWebView2;

                        e.Handled = true; // We handled it



                        // Hook close window request to close the WinForms popup Form automatically

                        popupWebView.CoreWebView2.WindowCloseRequested += (sender, args) =>

                        {

                            Log("[PDA] Popup window close requested by script.");

                            popupForm.Close();

                        };

                    }

                    catch (Exception ex)

                    {

                        Log("[PDA Error] Failed to initialize popup WebView2: " + ex.ToString());

                        e.Handled = true;

                        popupForm.Close();

                    }

                    finally

                    {

                        deferral.Complete();

                    }

                };



                // Inject CSS/JS to hide app download banners/popups, automatically click "Not now", and forward console messages

                string script = @"

                    (function() {

                        // App Store and download link helper

                        function isAppStoreUrl(url) {

                            if (!url) return false;

                            var lowerUrl = String(url).toLowerCase();

                            return lowerUrl.indexOf('apps.apple.com') >= 0 || 

                                   lowerUrl.indexOf('play.google.com') >= 0 || 

                                   lowerUrl.indexOf('itunes.apple.com') >= 0 || 

                                   lowerUrl.indexOf('onelink.me') >= 0 || 

                                   lowerUrl.indexOf('tiktok.com/download') >= 0 ||

                                   lowerUrl.indexOf('apple.co') >= 0 ||

                                   lowerUrl.indexOf('itms-apps') >= 0 ||

                                   lowerUrl.indexOf('market://') >= 0 ||

                                   (!lowerUrl.startsWith('http://') && !lowerUrl.startsWith('https://') && !lowerUrl.startsWith('about:') && !lowerUrl.startsWith('javascript:'));

                        }



                        // Override window.open

                        try {

                            var originalOpen = window.open;

                            window.open = function(url, target, features) {

                                if (isAppStoreUrl(url)) {

                                    console.log('BLOCKED window.open to: ' + url);

                                    return null;

                                }

                                return originalOpen.apply(this, arguments);

                            };

                        } catch(e) {

                            console.error('Failed to override window.open:', e);

                        }



                        // Override Location setters and methods

                        try {

                            var desc = Object.getOwnPropertyDescriptor(Location.prototype, 'href');

                            if (desc && desc.set) {

                                var originalSetHref = desc.set;

                                Object.defineProperty(Location.prototype, 'href', {

                                    set: function(url) {

                                        if (isAppStoreUrl(url)) {

                                            console.log('BLOCKED location.href set to: ' + url);

                                            return;

                                        }

                                        originalSetHref.call(this, url);

                                    },

                                    get: desc.get

                                });

                            }



                            var originalAssign = Location.prototype.assign;

                            Location.prototype.assign = function(url) {

                                if (isAppStoreUrl(url)) {

                                    console.log('BLOCKED location.assign to: ' + url);

                                    return;

                                }

                                return originalAssign.call(this, url);

                            };



                            var originalReplace = Location.prototype.replace;

                            Location.prototype.replace = function(url) {

                                if (isAppStoreUrl(url)) {

                                    console.log('BLOCKED location.replace to: ' + url);

                                    return;

                                }

                                return originalReplace.call(this, url);

                            };

                        } catch(e) {

                            console.error('Failed to override Location prototypes:', e);

                        }



                        // Capture clicks on App Store links

                        window.addEventListener('click', function(e) {

                            var target = e.target;

                            while (target && target.tagName !== 'A') {

                                target = target.parentElement;

                            }

                            if (target && target.tagName === 'A') {

                                var href = target.getAttribute('href');

                                if (isAppStoreUrl(href)) {

                                    console.log('BLOCKED click on link to: ' + href);

                                    e.preventDefault();

                                    e.stopPropagation();

                                }

                            }

                        }, true);



                        // Scrolling and clicking state tracking to manage video pause authorization

                        var isScrolling = false;

                        var isUserClick = false;

                        

                        var scrollTimeout;

                        function setScrolling() {

                            isScrolling = true;

                            clearTimeout(scrollTimeout);

                            scrollTimeout = setTimeout(function() {

                                isScrolling = false;

                            }, 1000);

                        }

                        window.addEventListener('scroll', setScrolling, true);

                        window.addEventListener('wheel', setScrolling, true);

                        window.addEventListener('touchmove', setScrolling, true);

                        

                        var clickTimeout;

                        window.addEventListener('mousedown', function(e) {

                            isUserClick = true;

                            clearTimeout(clickTimeout);

                            clickTimeout = setTimeout(function() {

                                isUserClick = false;

                            }, 300);

                        }, true);



                        // Overwrite document visibility and focus APIs to prevent TikTok from pausing on blur/hidden

                        try {

                            Object.defineProperty(document, 'visibilityState', {

                                get: function() { return 'visible'; }

                            });

                            Object.defineProperty(document, 'hidden', {

                                get: function() { return false; }

                            });

                            document.hasFocus = function() {

                                return true;

                            };



                            // Intercept and discard blur and focusout events on window and document

                            var originalAddEventListener = window.addEventListener;

                            window.addEventListener = function(type, listener, options) {

                                if (type === 'blur' || type === 'focusout') {

                                    return;

                                }

                                return originalAddEventListener.apply(this, arguments);

                            };



                            var originalDocAddEventListener = document.addEventListener;

                            document.addEventListener = function(type, listener, options) {

                                if (type === 'blur' || type === 'focusout' || type === 'visibilitychange') {

                                    return;

                                }

                                return originalDocAddEventListener.apply(this, arguments);

                            };



                            // Disable window.onblur and document.onblur properties

                            Object.defineProperty(window, 'onblur', {

                                set: function(val) { },

                                get: function() { return null; }

                            });

                            Object.defineProperty(document, 'onblur', {

                                set: function(val) { },

                                get: function() { return null; }

                            });

                        } catch(e) {

                            console.error('Focus override failed:', e);

                        }



                        // Hook video play/pause/load/src to log stack traces and prevent unauthorized pauses

                        try {

                            var originalPlay = HTMLVideoElement.prototype.play;

                            HTMLVideoElement.prototype.play = function() {

                                console.log('VIDEO PLAY CALLED on: ' + (this.src || this.currentSrc) + '\nStack: ' + new Error().stack);

                                return originalPlay.apply(this, arguments);

                            };



                            var originalPause = HTMLVideoElement.prototype.pause;

                            HTMLVideoElement.prototype.pause = function() {

                                var stack = new Error().stack || '';

                                var stackLower = stack.toLowerCase();

                                

                                // Only block pause if it is triggered by focus loss, window blur, or visibility changes

                                var isFocusBlurPause = stackLower.indexOf('blur') >= 0 || 

                                                       stackLower.indexOf('focus') >= 0 || 

                                                       stackLower.indexOf('visibility') >= 0;

                                

                                if (isFocusBlurPause) {

                                    console.log('BLOCKED focus/blur pause() | Stack: ' + stack.substring(0, 200));

                                    return;

                                }

                                

                                console.log('VIDEO PAUSE ALLOWED on: ' + (this.src || this.currentSrc) + ' | Stack: ' + stack.substring(0, 150));

                                return originalPause.apply(this, arguments);

                            };



                            var originalLoad = HTMLVideoElement.prototype.load;

                            HTMLVideoElement.prototype.load = function() {

                                console.log('VIDEO LOAD CALLED on: ' + (this.src || this.currentSrc) + '\nStack: ' + new Error().stack);

                                return originalLoad.apply(this, arguments);

                            };



                            var originalSrcDescriptor = Object.getOwnPropertyDescriptor(HTMLMediaElement.prototype, 'src');

                            if (originalSrcDescriptor && originalSrcDescriptor.set) {

                                Object.defineProperty(HTMLVideoElement.prototype, 'src', {

                                    set: function(val) {

                                        console.log('VIDEO SRC SET to: ' + val + '\nStack: ' + new Error().stack);

                                        originalSrcDescriptor.set.call(this, val);

                                    },

                                    get: function() {

                                        return originalSrcDescriptor.get.call(this);

                                    }

                                });

                            }

                        } catch(e) {

                            console.error('Video prototype hook failed: ' + e);

                        }



                        // Intercept beforeunload to block programmatic page navigation/redirects

                        window.addEventListener('beforeunload', function(e) {

                            e.preventDefault();

                            e.returnValue = 'Block';

                            return 'Block';

                        });



                        // Drag-to-scroll implementation for desktop layout inside narrow PDA window

                        let isDragging = false;

                        let startY = 0;

                        let scrollTop = 0;

                        

                        window.addEventListener('mousedown', function(e) {

                            // Do not initiate drag if user clicked an interactive element (button, link, input)

                            var tag = e.target.tagName.toLowerCase();

                            if (tag === 'button' || tag === 'a' || tag === 'input' || tag === 'svg' || tag === 'path' ||

                                e.target.closest('button') || e.target.closest('a') || e.target.closest('input')) {

                                return;

                            }

                            

                            isDragging = true;

                            startY = e.clientY;

                            scrollTop = window.pageYOffset || document.documentElement.scrollTop;

                            document.body.style.cursor = 'grabbing';

                        });



                        window.addEventListener('mousemove', function(e) {

                            if (!isDragging) return;

                            e.preventDefault();

                            const y = e.clientY;

                            const walk = (y - startY) * 1.5; // Drag speed multiplier

                            window.scrollTo(0, scrollTop - walk);

                        });



                        window.addEventListener('mouseup', function() {

                            if (isDragging) {

                                isDragging = false;

                                document.body.style.cursor = 'default';

                            }

                        });



                        window.addEventListener('mouseleave', function() {

                            if (isDragging) {

                                isDragging = false;

                                document.body.style.cursor = 'default';

                            }

                        });



                        // Forward console logs to C#

                        var originalLog = console.log;

                        console.log = function() {

                            var args = Array.prototype.slice.call(arguments);

                            window.chrome.webview.postMessage(JSON.stringify({ type: 'log', msg: args.join(' ') }));

                            if (originalLog) originalLog.apply(console, arguments);

                        };

                        var originalError = console.error;

                        console.error = function() {

                            var args = Array.prototype.slice.call(arguments);

                            window.chrome.webview.postMessage(JSON.stringify({ type: 'error', msg: args.join(' ') }));

                            if (originalError) originalError.apply(console, arguments);

                        };

                        var originalWarn = console.warn;

                        console.warn = function() {

                            var args = Array.prototype.slice.call(arguments);

                            window.chrome.webview.postMessage(JSON.stringify({ type: 'warn', msg: args.join(' ') }));

                            if (originalWarn) originalWarn.apply(console, arguments);

                        };



                        window.onerror = function(message, source, lineno, colno, error) {

                            window.chrome.webview.postMessage(JSON.stringify({

                                type: 'onerror',

                                msg: message + ' at ' + source + ':' + lineno + ':' + colno

                            }));

                            return false;

                        };



                        function inject() {

                            var style = document.createElement('style');

                            style.innerHTML = `

                        function inject() {

                            var isLoginOrSignup = window.location.href.indexOf('/login') >= 0 || window.location.href.indexOf('/signup') >= 0;

                            var style = document.createElement('style');

                            if (isLoginOrSignup) {

                                style.innerHTML = `

                                    html, body, #app, main {

                                        background-color: #000000 !important;

                                        background: #000000 !important;

                                    }

                                    /* Force top-level containers to have a strict width of 490px and height 700px */

                                    html, body, #app, [class*=""BaseBodyContainer""], [class*=""DivBodyContainer""] {

                                        width: 490px !important;

                                        height: 700px !important;

                                        max-width: 490px !important;

                                        max-height: 700px !important;

                                        margin: 0 auto !important;

                                        padding: 0 !important;

                                        position: relative !important;

                                        display: block !important;

                                        overflow-y: auto !important;

                                    }

                                    /* Keep login inputs, form card visible and centered */

                                    [class*=""DivLoginContainer""], [class*=""DivGateContainer""], [class*=""login""], [class*=""Login""], [class*=""signup""], [class*=""Signup""] {

                                        width: 100% !important;

                                        max-width: 490px !important;

                                        margin: 0 auto !important;

                                        display: block !important;

                                        position: relative !important;

                                        opacity: 1 !important;

                                        visibility: visible !important;

                                        pointer-events: auto !important;

                                    }

                                    /* Hide app download banners and header/footer */

                                    header, [class*=""header""], [class*=""Header""],

                                    [class*=""download""], [class*=""Download""], [class*=""banner""], [class*=""Banner""],

                                    a[href*=""apps.apple.com""], a[href*=""play.google.com""], a[href*=""onelink.me""] {

                                        display: none !important;

                                    }

                                    /* Hide scrollbars */

                                    html::-webkit-scrollbar, body::-webkit-scrollbar {

                                        display: none !important;

                                    }



                                    /* Make the left navigation sidebar thinner and fit its items */

                                    [class*=""DivSideNavContainer""], 

                                    [class*=""DivMainNavContainer""], 

                                    [class*=""DivSidebarContainer""],

                                    [class*=""SideNavContainer""],

                                    [class*=""MainNavContainer""],

                                    [class*=""SidebarContainer""],

                                    [class*=""SideNav""], 

                                    [class*=""SideBar""] {

                                        width: 50px !important;

                                        min-width: 50px !important;

                                        max-width: 50px !important;

                                        padding: 0 !important;

                                        margin: 0 !important;

                                    }



                                    /* Fit elements inside the thinner sidebar */

                                    [class*=""DivSideNavContainer""] *, 

                                    [class*=""DivMainNavContainer""] *, 

                                    [class*=""DivSidebarContainer""] *,

                                    [class*=""SideNavContainer""] *,

                                    [class*=""MainNavContainer""] *,

                                    [class*=""SidebarContainer""] *,

                                    [class*=""SideNav""] *,

                                    [class*=""SideBar""] * {

                                        max-width: 100% !important;

                                        box-sizing: border-box !important;

                                    }



                                    /* Clean up link/button paddings and center them in the sidebar */

                                    [class*=""DivSideNavContainer""] a,

                                    [class*=""DivMainNavContainer""] a,

                                    [class*=""DivSidebarContainer""] a,

                                    [class*=""SideNavContainer""] a,

                                    [class*=""MainNavContainer""] a,

                                    [class*=""SidebarContainer""] a,

                                    [class*=""SideNav""] a,

                                    [class*=""SideBar""] a,

                                    [class*=""DivSideNavContainer""] button,

                                    [class*=""DivMainNavContainer""] button,

                                    [class*=""DivSidebarContainer""] button,

                                    [class*=""SideNavContainer""] button,

                                    [class*=""MainNavContainer""] button,

                                    [class*=""SidebarContainer""] button,

                                    [class*=""SideNav""] button,

                                    [class*=""SideBar""] button {

                                        padding: 6px 2px !important;

                                        margin: 4px 0 !important;

                                        width: 100% !important;

                                        min-width: 0 !important;

                                        display: flex !important;

                                        justify-content: center !important;

                                        align-items: center !important;

                                    }



                                    /* Adjust icon sizes inside the sidebar to fit nicely */

                                    [class*=""DivSideNavContainer""] svg,

                                    [class*=""DivMainNavContainer""] svg,

                                    [class*=""DivSidebarContainer""] svg,

                                    [class*=""SideNav""] svg,

                                    [class*=""SideBar""] svg {

                                        width: 22px !important;

                                        height: 22px !important;

                                    }

                                `;

                            } else {

                                style.innerHTML = `

                                    /* Hide header, footer, app download banners/popups, etc. (Sidebars are kept visible but styled thinner) */

                                    header, footer,

                                    [class*=""header""], [class*=""Header""],

                                    [class*=""download""], [class*=""Download""],

                                    [class*=""banner""], [class*=""Banner""],

                                    [class*=""AppOpen""], [class*=""app-open""],

                                    [class*=""AppInstall""], [class*=""app-install""],

                                    a[href*=""apps.apple.com""], a[href*=""play.google.com""],

                                    a[href*=""onelink.me""] {

                                        display: none !important;

                                        width: 0 !important;

                                        height: 0 !important;

                                        visibility: hidden !important;

                                        pointer-events: none !important;

                                        opacity: 0 !important;

                                    }



                                    /* Position modals off-screen so they can still receive programmatic clicks, 

                                       but keep them visible if they contain input elements (login/signup forms) */

                                    [class*=""popup""]:not(:has(input)), [class*=""Popup""]:not(:has(input)),

                                    [class*=""modal""]:not(:has(input)), [class*=""Modal""]:not(:has(input)),

                                    [class*=""tux-modal""]:not(:has(input)), [class*=""tux-dialog""]:not(:has(input)),

                                    [class*=""tux-popup""]:not(:has(input)), [class*=""tux-toast""],

                                    [class*=""login""]:not(:has(input)), [class*=""Login""]:not(:has(input)),

                                    [class*=""signup""]:not(:has(input)), [class*=""Signup""]:not(:has(input)),

                                    [class*=""gate""]:not(:has(input)), [class*=""Gate""]:not(:has(input)) {

                                        position: absolute !important;

                                        left: -9999px !important;

                                        top: -9999px !important;

                                        opacity: 0 !important;

                                        pointer-events: none !important;

                                        width: auto !important;

                                        height: auto !important;

                                        visibility: visible !important;

                                    }



                                    /* Ensure black background for all elements */

                                    html, body, #app, main, [class*=""BaseBodyContainer""], [class*=""DivBodyContainer""] {

                                        background-color: #000000 !important;

                                        background: #000000 !important;

                                    }



                                    /* Force top-level containers to have a strict width of 490px */

                                    html, body, #app, [class*=""BaseBodyContainer""], [class*=""DivBodyContainer""] {

                                        width: 490px !important;

                                        max-width: 490px !important;

                                        min-width: 490px !important;

                                        margin: 0 auto !important;

                                        padding: 0 !important;

                                        position: relative !important;

                                        display: block !important;

                                    }



                                    /* Force article to fill exactly 700px vertically and 490px horizontally */

                                    [class*=""ArticleItemContainer""] {

                                        height: 700px !important;

                                        min-height: 700px !important;

                                        max-height: 700px !important;

                                        width: 490px !important;

                                        max-width: 490px !important;

                                        min-width: 490px !important;

                                        margin: 0 auto !important;

                                        padding: 0 !important;

                                        position: relative !important;

                                        display: block !important;

                                    }



                                    /* Force flex layout to be absolute full-screen inside article */

                                    [class*=""DivContentFlexLayout""] {

                                        width: 490px !important;

                                        height: 700px !important;

                                        max-width: 490px !important;

                                        max-height: 700px !important;

                                        position: absolute !important;

                                        top: 0 !important;

                                        left: 0 !important;

                                        margin: 0 !important;

                                        padding: 0 !important;

                                        display: block !important;

                                    }



                                    /* Force media card (video player wrapper) to occupy the entire viewport */

                                    [class*=""SectionMediaCardContainer""] {

                                        width: 490px !important;

                                        height: 700px !important;

                                        max-width: 490px !important;

                                        max-height: 700px !important;

                                        min-width: 490px !important;

                                        min-height: 700px !important;

                                        position: absolute !important;

                                        top: 0 !important;

                                        left: 0 !important;

                                        margin: 0 !important;

                                        padding: 0 !important;

                                    }



                                    /* Force video element to fit nicely inside the container */

                                    [class*=""SectionMediaCardContainer""] video {

                                        width: 100% !important;

                                        height: 100% !important;

                                        object-fit: contain !important;

                                    }



                                    /* Reposition interaction action buttons to bottom-right corner over the video player with Glassmorphism and Neon Cyan glow */

                                    [class*=""SectionActionBarContainer""] {

                                        position: absolute !important;

                                        bottom: 70px !important;

                                        right: 10px !important;

                                        z-index: 999 !important;

                                        background: rgba(10, 15, 20, 0.65) !important;

                                        backdrop-filter: blur(10px) !important;

                                        -webkit-backdrop-filter: blur(10px) !important;

                                        border: 1px solid rgba(0, 255, 220, 0.3) !important;

                                        border-radius: 20px !important;

                                        padding: 12px 6px !important;

                                        box-shadow: 0 0 15px rgba(0, 255, 220, 0.25) !important;

                                        display: flex !important;

                                        flex-direction: column !important;

                                        align-items: center !important;

                                        gap: 10px !important;

                                    }



                                    /* Custom hover animations for the actions */

                                    [class*=""SectionActionBarContainer""] button,

                                    [class*=""SectionActionBarContainer""] [role=""button""] {

                                        background: transparent !important;

                                        border: none !important;

                                        transition: all 0.25s cubic-bezier(0.175, 0.885, 0.32, 1.275) !important;

                                        cursor: pointer !important;

                                    }

                                    [class*=""SectionActionBarContainer""] button:hover,

                                    [class*=""SectionActionBarContainer""] [role=""button""]:hover {

                                        transform: scale(1.15) !important;

                                        filter: drop-shadow(0 0 5px rgba(0, 255, 220, 0.8)) !important;

                                    }



                                    /* Neon recoloring for SVG icons */

                                    [class*=""SectionActionBarContainer""] svg {

                                        fill: #00ffd2 !important;

                                        color: #00ffd2 !important;

                                    }



                                    /* Courier style for numbers/counters */

                                    [class*=""SectionActionBarContainer""] strong {

                                        color: #ffffff !important;

                                        font-family: 'Courier New', Courier, monospace !important;

                                        font-size: 11px !important;

                                        text-shadow: 0 0 4px rgba(0, 255, 220, 0.6) !important;

                                        letter-spacing: 0.5px !important;

                                        margin-top: 2px !important;

                                        font-weight: 700 !important;

                                    }



                                    /* Reposition and style video info (user, description, music) as a sleek sci-fi terminal box */

                                    [class*=""DivVideoInfoContainer""],

                                    [class*=""DivDescription""],

                                    [class*=""DivVideoDescription""] {

                                        position: absolute !important;

                                        bottom: 70px !important;

                                        left: 10px !important;

                                        width: 330px !important;

                                        z-index: 999 !important;

                                        background: rgba(10, 15, 20, 0.65) !important;

                                        backdrop-filter: blur(10px) !important;

                                        -webkit-backdrop-filter: blur(10px) !important;

                                        border-left: 2px solid #00ffd2 !important;

                                        border-top: 1px solid rgba(0, 255, 220, 0.2) !important;

                                        border-right: 1px solid rgba(0, 255, 220, 0.2) !important;

                                        border-bottom: 1px solid rgba(0, 255, 220, 0.2) !important;

                                        border-radius: 0 12px 12px 0 !important;

                                        padding: 10px 14px !important;

                                        box-shadow: 0 0 15px rgba(0, 255, 220, 0.2) !important;

                                        color: #e0f7f4 !important;

                                        font-family: 'Courier New', Courier, monospace !important;

                                        box-sizing: border-box !important;

                                    }



                                    /* Highlight username and tags in glowing cyan/teal */

                                    [class*=""DivVideoInfoContainer""] a,

                                    [class*=""DivVideoInfoContainer""] h3,

                                    [class*=""DivVideoInfoContainer""] h4,

                                    [class*=""DivDescription""] a,

                                    [class*=""DivVideoDescription""] a {

                                        color: #00ffd2 !important;

                                        text-decoration: none !important;

                                        font-weight: bold !important;

                                        text-shadow: 0 0 5px rgba(0, 255, 220, 0.7) !important;

                                    }



                                    /* Style music/sound text with a glowing icon effect */

                                    [class*=""DivMusicText""], [class*=""DivMusic""], [class*=""DivSound""], [class*=""DivMusicInfo""] {

                                        color: #88a8a4 !important;

                                        font-size: 11px !important;

                                        margin-top: 8px !important;

                                        display: flex !important;

                                        align-items: center !important;

                                        font-family: 'Courier New', Courier, monospace !important;

                                    }



                                    /* Hide scrollbars */

                                    html::-webkit-scrollbar, body::-webkit-scrollbar {

                                        display: none !important;

                                    }



                                    /* Make the left navigation sidebar thinner and fit its items */

                                    [class*=""DivSideNavContainer""], 

                                    [class*=""DivMainNavContainer""], 

                                    [class*=""DivSidebarContainer""],

                                    [class*=""SideNavContainer""],

                                    [class*=""MainNavContainer""],

                                    [class*=""SidebarContainer""],

                                    [class*=""SideNav""], 

                                    [class*=""SideBar""] {

                                        width: 50px !important;

                                        min-width: 50px !important;

                                        max-width: 50px !important;

                                        padding: 0 !important;

                                        margin: 0 !important;

                                    }



                                    /* Fit elements inside the thinner sidebar */

                                    [class*=""DivSideNavContainer""] *, 

                                    [class*=""DivMainNavContainer""] *, 

                                    [class*=""DivSidebarContainer""] *,

                                    [class*=""SideNavContainer""] *,

                                    [class*=""MainNavContainer""] *,

                                    [class*=""SidebarContainer""] *,

                                    [class*=""SideNav""] *,

                                    [class*=""SideBar""] * {

                                        max-width: 100% !important;

                                        box-sizing: border-box !important;

                                    }



                                    /* Clean up link/button paddings and center them in the sidebar */

                                    [class*=""DivSideNavContainer""] a,

                                    [class*=""DivMainNavContainer""] a,

                                    [class*=""DivSidebarContainer""] a,

                                    [class*=""SideNavContainer""] a,

                                    [class*=""MainNavContainer""] a,

                                    [class*=""SidebarContainer""] a,

                                    [class*=""SideNav""] a,

                                    [class*=""SideBar""] a,

                                    [class*=""DivSideNavContainer""] button,

                                    [class*=""DivMainNavContainer""] button,

                                    [class*=""DivSidebarContainer""] button,

                                    [class*=""SideNavContainer""] button,

                                    [class*=""MainNavContainer""] button,

                                    [class*=""SidebarContainer""] button,

                                    [class*=""SideNav""] button,

                                    [class*=""SideBar""] button {

                                        padding: 6px 2px !important;

                                        margin: 4px 0 !important;

                                        width: 100% !important;

                                        min-width: 0 !important;

                                        display: flex !important;

                                        justify-content: center !important;

                                        align-items: center !important;

                                    }



                                    /* Adjust icon sizes inside the sidebar to fit nicely */

                                    [class*=""DivSideNavContainer""] svg,

                                    [class*=""DivMainNavContainer""] svg,

                                    [class*=""DivSidebarContainer""] svg,

                                    [class*=""SideNav""] svg,

                                    [class*=""SideBar""] svg {

                                        width: 22px !important;

                                        height: 22px !important;

                                    }

                                `;

                            }

                            var container = document.head || document.documentElement;

                            if (container) {

                                container.appendChild(style);

                            }

                        }



                        if (document.head || document.documentElement) {

                            inject();

                        } else {

                            document.addEventListener('DOMContentLoaded', inject);

                        }



                        // Dump video player layout tree and article children to log file

                        function dumpLayout() {

                            setTimeout(function() {

                                var video = document.querySelector('video');

                                if (video) {

                                    var path = [];

                                    var parent = video;

                                    while (parent && parent.tagName !== 'BODY') {

                                        var info = parent.tagName;

                                        if (parent.id) info += '#' + parent.id;

                                        if (parent.className && typeof parent.className === 'string') {

                                            info += '.' + parent.className.split(' ').join('.');

                                        }

                                        var rect = parent.getBoundingClientRect();

                                        var style = window.getComputedStyle(parent);

                                        info += ' (' + Math.round(rect.width) + 'x' + Math.round(rect.height) + ' at ' + Math.round(rect.left) + ',' + Math.round(rect.top) + ')';

                                        info += ' { width:' + style.width + ', height:' + style.height + ', max-width:' + style.maxWidth + ', min-width:' + style.minWidth + ', margin:' + style.margin + ', padding:' + style.padding + ', position:' + style.position + ', top:' + style.top + ', display:' + style.display + ' }';

                                        path.push(info);

                                        parent = parent.parentElement;

                                    }

                                    console.log('VIDEO PATH:\n' + path.reverse().join('\n -> '));

                                } else {

                                    console.log('No video element found yet.');

                                }



                                var flexLayout = document.querySelector('[class*=""DivContentFlexLayout""]');

                                if (flexLayout) {

                                    var flexChildren = [];

                                    for (var i = 0; i < flexLayout.children.length; i++) {

                                        var el = flexLayout.children[i];

                                        var rect = el.getBoundingClientRect();

                                        var style = window.getComputedStyle(el);

                                        var info = el.tagName;

                                        if (el.id) info += '#' + el.id;

                                        if (el.className && typeof el.className === 'string') {

                                            info += '.' + el.className.split(' ').join('.');

                                        }

                                        info += ' (' + Math.round(rect.width) + 'x' + Math.round(rect.height) + ' at ' + Math.round(rect.left) + ',' + Math.round(rect.top) + ')';

                                        info += ' { width:' + style.width + ', height:' + style.height + ', display:' + style.display + ', position:' + style.position + ' }';

                                        flexChildren.push(info);

                                    }

                                    console.log('FLEX LAYOUT CHILDREN:\n' + flexChildren.join('\n'));

                                }

                            }, 3000);

                        }

                        if (document.readyState === 'complete') {

                            dumpLayout();

                        } else {

                            window.addEventListener('load', dumpLayout);

                        }



                        function instrumentVideo(video) {

                            if (video.__instrumented) return;

                            video.__instrumented = true;

                            console.log('Instrumenting video element: ' + (video.src || video.currentSrc));

                            

                            var events = ['play', 'playing', 'pause', 'waiting', 'error', 'emptied', 'loadstart', 'loadedmetadata', 'suspend', 'abort', 'stalled'];

                            events.forEach(function(ev) {

                                video.addEventListener(ev, function() {

                                    console.log('VIDEO EVENT: ' + ev + ' on: ' + (video.src || video.currentSrc) + ' | Paused: ' + video.paused + ' | Muted: ' + video.muted + ' | ReadyState: ' + video.readyState);

                                });

                            });

                        }



                        setInterval(function() {

                            // Safely instrument all video elements and enforce mute for autoplay

                            document.querySelectorAll('video').forEach(instrumentVideo);

                            // Dynamic sidebar thinning logic
                            var sidebarWidth = 48;
                            var sidebars = [];
                            document.querySelectorAll('div, nav, aside').forEach(function(el) {
                                var rect = el.getBoundingClientRect();
                                if (rect.left >= 0 && rect.left <= 10 && rect.width >= 60 && rect.width <= 95 && rect.height > 300) {
                                    sidebars.push(el);
                                    el.style.setProperty('width', sidebarWidth + 'px', 'important');
                                    el.style.setProperty('min-width', sidebarWidth + 'px', 'important');
                                    el.style.setProperty('max-width', sidebarWidth + 'px', 'important');
                                    
                                    el.querySelectorAll('a, button, [role=""button""], [role=""link""]').forEach(function(child) {
                                        child.style.setProperty('padding', '6px 2px', 'important');
                                        child.style.setProperty('margin', '4px 0', 'important');
                                        child.style.setProperty('width', '100%', 'important');
                                        child.style.setProperty('min-width', '0', 'important');
                                        child.style.setProperty('display', 'flex', 'important');
                                        child.style.setProperty('justify-content', 'center', 'important');
                                        child.style.setProperty('align-items', 'center', 'important');
                                    });
                                    
                                    el.querySelectorAll('svg').forEach(function(svg) {
                                        svg.style.setProperty('width', '22px', 'important');
                                        svg.style.setProperty('height', '22px', 'important');
                                    });
                                }
                            });

                            document.querySelectorAll('div, main, section, article, header, footer').forEach(function(el) {
                                var insideSidebar = false;
                                for (var i = 0; i < sidebars.length; i++) {
                                    if (sidebars[i].contains(el)) {
                                        insideSidebar = true;
                                        break;
                                    }
                                }
                                if (insideSidebar) return;

                                var style = window.getComputedStyle(el);
                                
                                var marginLeft = parseFloat(style.marginLeft) || 0;
                                if (marginLeft >= 60 && marginLeft <= 95) {
                                    el.style.setProperty('margin-left', sidebarWidth + 'px', 'important');
                                }
                                
                                var paddingLeft = parseFloat(style.paddingLeft) || 0;
                                if (paddingLeft >= 60 && paddingLeft <= 95) {
                                    el.style.setProperty('padding-left', sidebarWidth + 'px', 'important');
                                }

                                var left = style.position !== 'static' ? (parseFloat(style.left) || 0) : 0;
                                if (style.position !== 'static' && left >= 60 && left <= 95) {
                                    el.style.setProperty('left', sidebarWidth + 'px', 'important');
                                }
                            });



                            // Automatically hide app store link wrappers safely (only 1 level parent)

                            var appLinks = document.querySelectorAll('a[href*=""apps.apple.com""], a[href*=""play.google.com""], a[href*=""onelink.me""]');

                            appLinks.forEach(function(link) {

                                link.style.setProperty('display', 'none', 'important');

                                var parent = link.parentElement;

                                if (parent) {

                                    parent.style.setProperty('display', 'none', 'important');

                                }

                            });



                            // Click 'Not now' buttons automatically (only leaf/specific small elements to avoid clicking wrappers/video containers)

                            var elements = document.querySelectorAll('button, a, [role=""button""], div, span');

                            var targets = ['not now', 'не зараз', 'не сейчас', 'пізніше', 'позже', 'потім'];

                            elements.forEach(function(el) {

                                var text = (el.innerText || el.textContent || '').trim().toLowerCase();

                                if (targets.indexOf(text) >= 0) {

                                    var isInteractive = el.tagName === 'BUTTON' || el.tagName === 'A' || el.getAttribute('role') === 'button';

                                    var isLeaf = el.children.length === 0;

                                    if (isInteractive || isLeaf) {

                                        console.log('AUTO-CLICKING element:', el.tagName, el.className, 'Text:', text);

                                        el.click();

                                    }

                                }

                            });



                            // Auto-click close buttons to dismiss login/signup/app download popups

                            var closeButtons = document.querySelectorAll([

                                '[data-e2e=""close-button""]',

                                '[aria-label=""Close""]',

                                '[aria-label=""close""]',

                                '[class*=""close-btn""]',

                                '[class*=""CloseBtn""]',

                                '[class*=""closeIcon""]',

                                '[class*=""CloseIcon""]',

                                '[class*=""close-icon""]',

                                '[class*=""close_icon""]',

                                '.tux-Modal-close',

                                '.tux-Dialog-close'

                            ].join(','));

                            

                            closeButtons.forEach(function(btn) {

                                // Do not auto-click close if the container modal contains input elements (login form)

                                var parentModal = btn.closest('[class*=""modal""], [class*=""Modal""], [class*=""dialog""], [class*=""Dialog""], [class*=""popup""], [class*=""Popup""]');

                                if (parentModal && parentModal.querySelector('input')) {

                                    return;

                                }

                                if (btn && typeof btn.click === 'function') {

                                    console.log('AUTO-CLICKING close button: ' + btn.className);

                                    btn.click();

                                }

                            });



                            // Auto-play the active video if it is paused (not user-clicked, not scrolling, not ended)

                            var activeVideo = null;

                            document.querySelectorAll('video').forEach(function(video) {

                                var rect = video.getBoundingClientRect();

                                if (rect.top >= -200 && rect.top <= 200) {

                                    activeVideo = video;

                                }

                            });



                            if (activeVideo && activeVideo.paused && !isUserClick && !isScrolling && !activeVideo.ended) {

                                console.log('AUTO-PLAYING active video which was paused: ' + (activeVideo.src || activeVideo.currentSrc));

                                activeVideo.play().catch(function(err) {

                                    console.error('Auto-play failed: ' + err.message);

                                });

                            }



                            // Enforce scrollability on top-level body and html elements only

                            if (document.body) {

                                document.body.style.setProperty('overflow', 'auto', 'important');

                                document.body.style.setProperty('position', 'static', 'important');

                            }

                            if (document.documentElement) {

                                document.documentElement.style.setProperty('overflow', 'auto', 'important');

                            }

                        }, 500);

                    })();

                ";

                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);





                webView.NavigationStarting += (s, e) => 

                { 

                    isLoading = true; 

                    this.Invalidate(new Rectangle(430, 10, 100, 50)); 

                    

                    string uri = e.Uri.ToString();

                    string lowerUri = uri.ToLower();

                    Log("[PDA] Navigation starting: " + uri);

                    

                    bool isAppStoreOrDownload = lowerUri.Contains("apps.apple.com") || 

                                                lowerUri.Contains("play.google.com") || 

                                                lowerUri.Contains("itunes.apple.com") || 

                                                lowerUri.Contains("onelink.me") || 

                                                lowerUri.Contains("tiktok.com/download") ||

                                                lowerUri.Contains("apple.co");

                                                

                    bool isCustomProtocol = !lowerUri.StartsWith("http://") && !lowerUri.StartsWith("https://");

                    

                    if (isAppStoreOrDownload || isCustomProtocol || lowerUri.StartsWith("itms-apps") || lowerUri.Contains("market://"))

                    {

                        if (isCustomProtocol || lowerUri.StartsWith("itms-apps"))

                        {

                            e.Cancel = true; // Safe to cancel custom protocols immediately without white screen

                            Log("[PDA] Instantly blocked custom protocol: " + uri);

                        }

                        else

                        {

                            shouldBlockUnload = true; // Let it trigger beforeunload so we can block it without a white screen

                            Log("[PDA] Redirect detected. Enabled BeforeUnload interception for: " + uri);

                        }

                    }

                    else

                    {

                        shouldBlockUnload = false;

                    }

                };



                webView.NavigationCompleted += (s, e) => 

                { 

                    isLoading = false; 

                    this.Invalidate(new Rectangle(430, 10, 100, 50)); 

                    Log(string.Format("[PDA] Navigation completed: {0} (Success: {1}, Error: {2})", webView.Source, e.IsSuccess, e.WebErrorStatus));

                };



                webView.Source = new Uri(InitialUrl);

            }

            catch (Exception ex)

            {

                Log("[PDA Error] Failed to initialize WebView2: " + ex.ToString());

                MessageBox.Show("Failed to initialize WebView2: " + ex.Message + "\nMake sure Microsoft Edge WebView2 Runtime is installed.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                Application.Exit();

            }

        }



        private void InitializeLedTimer()

        {

            ledTimer = new Timer

            {

                Interval = 500

            };

            ledTimer.Tick += (s, e) =>

            {

                ledState = !ledState;

                // Invalidate the LED area to repaint

                this.Invalidate(new Rectangle(25, 15, 30, 30));

            };

            ledTimer.Start();

        }



        protected override void OnMouseDown(MouseEventArgs e)

        {

            base.OnMouseDown(e);



            // Drag window if clicked on the top or side bezels

            if (e.Button == MouseButtons.Left)

            {

                if (e.Y < 65 || e.Y > 765 || e.X < 25 || e.X > 515)

                {

                    // Check if clicked inside a button bounds

                    foreach (var button in buttons)

                    {

                        if (button.Bounds.Contains(e.Location))

                        {

                            button.Action();

                            return;

                        }

                    }



                    ReleaseCapture();

                    SendMessage(Handle, 0xA1, 0x2, 0);

                }

            }

        }



        protected override void OnMouseMove(MouseEventArgs e)

        {

            base.OnMouseMove(e);

            mousePos = e.Location;



            bool needsRepaint = false;

            foreach (var button in buttons)

            {

                bool hovered = button.Bounds.Contains(mousePos);

                if (hovered != button.IsHovered)

                {

                    button.IsHovered = hovered;

                    needsRepaint = true;

                }

            }



            if (needsRepaint)

            {

                this.Invalidate(new Rectangle(20, 770, 500, 90));

            }

        }



        protected override void OnMouseLeave(EventArgs e)

        {

            base.OnMouseLeave(e);

            bool needsRepaint = false;

            foreach (var button in buttons)

            {

                if (button.IsHovered)

                {

                    button.IsHovered = false;

                    needsRepaint = true;

                }

            }

            if (needsRepaint)

            {

                this.Invalidate(new Rectangle(20, 770, 500, 90));

            }

        }



        protected override void OnPaint(PaintEventArgs e)

        {

            base.OnPaint(e);

            Graphics g = e.Graphics;

            g.SmoothingMode = SmoothingMode.AntiAlias;



            // --- BEZEL BACKGROUND ---

            // Draw dark metallic border gradient

            using (LinearGradientBrush brush = new LinearGradientBrush(this.ClientRectangle, 

                Color.FromArgb(40, 44, 52), Color.FromArgb(20, 22, 26), 45F))

            {

                g.FillRectangle(brush, this.ClientRectangle);

            }



            // Draw inner screen casing border

            using (Pen pen = new Pen(Color.FromArgb(12, 13, 15), 3))

            {

                g.DrawRectangle(pen, 23, 63, 494, 704);

            }



            // Outer border line

            using (Pen pen = new Pen(Color.FromArgb(60, 65, 75), 2))

            {

                // Top/sides outer highlights

                g.DrawArc(pen, 1, 1, 30, 30, 180, 90);

                g.DrawLine(pen, 16, 1, Width - 16, 1);

                g.DrawArc(pen, Width - 31, 1, 30, 30, 270, 90);

                g.DrawLine(pen, 1, 16, 1, Height - 16);

                g.DrawLine(pen, Width - 1, 16, Width - 1, Height - 16);

            }



            // --- SPEAKER GRILLE ---

            using (Pen pen = new Pen(Color.FromArgb(15, 15, 15), 2))

            {

                for (int i = 0; i < 7; i++)

                {

                    int xStart = 200 + i * 20;

                    g.DrawLine(pen, xStart, 28, xStart + 10, 28);

                    g.DrawLine(pen, xStart - 5, 34, xStart + 5, 34);

                }

            }



            // --- LED INDICATOR ---

            // Active Blinking LED

            Color ledColor = ledState ? Color.FromArgb(0, 255, 180) : Color.FromArgb(0, 60, 45);

            using (SolidBrush brush = new SolidBrush(ledColor))

            {

                g.FillEllipse(brush, 35, 23, 14, 14);

            }

            // Glow overlay if LED is active

            if (ledState)

            {

                using (PathGradientBrush rgb = CreateRadialBrush(new PointF(42, 30), 12, Color.FromArgb(100, 0, 255, 180), Color.Transparent))

                {

                    ColorBlend cb = new ColorBlend(3);

                    cb.Colors = new Color[] { Color.FromArgb(100, 0, 255, 180), Color.FromArgb(30, 0, 255, 180), Color.Transparent };

                    cb.Positions = new float[] { 0.0f, 0.4f, 1.0f };

                    rgb.InterpolationColors = cb;

                    g.FillEllipse(rgb, 27, 15, 30, 30);

                }

            }

            // Static Power LED

            using (SolidBrush brush = new SolidBrush(Color.FromArgb(200, 30, 30)))

            {

                g.FillEllipse(brush, 60, 23, 8, 8);

            }



            // --- SYSTEM TEXT ---

            using (Font font = new Font("Courier New", 9, FontStyle.Bold))

            using (SolidBrush brush = new SolidBrush(Color.FromArgb(130, 140, 150)))

            {

                g.DrawString("EUROPA-OS v4.11", font, brush, 80, 21);

            }



            // Loading status text

            if (isLoading)

            {

                using (Font font = new Font("Courier New", 8, FontStyle.Bold))

                using (SolidBrush brush = new SolidBrush(Color.FromArgb(0, 255, 220)))

                {

                    g.DrawString("[NET SYNCING]", font, brush, 410, 22);

                }

            }

            else

            {

                using (Font font = new Font("Courier New", 8, FontStyle.Bold))

                using (SolidBrush brush = new SolidBrush(Color.FromArgb(100, 110, 120)))

                {

                    g.DrawString("[SIGNAL OK]", font, brush, 420, 22);

                }

            }



            // --- DRAW BUTTONS ---

            foreach (var button in buttons)

            {

                // Draw button border and fill

                Color fill = button.IsHovered 

                    ? Color.FromArgb(45, 48, 56) 

                    : Color.FromArgb(30, 32, 38);

                

                using (SolidBrush brush = new SolidBrush(fill))

                {

                    g.FillRectangle(brush, button.Bounds);

                }



                Color border = button.IsHovered ? button.GlowColor : button.BorderColor;

                using (Pen pen = new Pen(border, button.IsHovered ? 2 : 1))

                {

                    g.DrawRectangle(pen, button.Bounds);

                }



                // Draw button text

                using (Font font = new Font("Courier New", 9, FontStyle.Bold))

                using (SolidBrush brush = new SolidBrush(button.IsHovered ? button.GlowColor : Color.FromArgb(180, 190, 200)))

                {

                    SizeF textSize = g.MeasureString(button.Label, font);

                    float xText = button.Bounds.X + (button.Bounds.Width - textSize.Width) / 2;

                    float yText = button.Bounds.Y + (button.Bounds.Height - textSize.Height) / 2;

                    g.DrawString(button.Label, font, brush, xText, yText);

                }



                // Draw neon glow for hovered button

                if (button.IsHovered)

                {

                    using (Pen pen = new Pen(Color.FromArgb(50, button.GlowColor), 4))

                    {

                        var expanded = button.Bounds;

                        expanded.Inflate(2, 2);

                        g.DrawRectangle(pen, expanded);

                    }

                }

            }

        }

        private static PathGradientBrush CreateRadialBrush(PointF center, float radius, Color centerColor, Color surroundColor)

        {

            GraphicsPath path = new GraphicsPath();

            path.AddEllipse(center.X - radius, center.Y - radius, radius * 2, radius * 2);

            PathGradientBrush brush = new PathGradientBrush(path);

            brush.CenterPoint = center;

            brush.CenterColor = centerColor;

            brush.SurroundColors = new Color[] { surroundColor };

            return brush;

        }

    }



    public class PdaButton

    {

        public Rectangle Bounds { get; set; }

        public string Label { get; set; }

        public Color BorderColor { get; set; }

        public Color GlowColor { get; set; }

        public Action Action { get; set; }

        public bool IsHovered { get; set; }

    }



    static class Program

    {

        [STAThread]

        static void Main(string[] args)

        {

            Console.WriteLine("[PDA] Main entry point hit.");

            Application.EnableVisualStyles();

            Application.SetCompatibleTextRenderingDefault(false);



            IntPtr parent = IntPtr.Zero;

            long longValue;

            if (args.Length > 0 && long.TryParse(args[0], out longValue))

            {

                parent = new IntPtr(longValue);

                Console.WriteLine("[PDA] Parent window handle: " + parent);

            }



            Console.WriteLine("[PDA] Running application message loop...");

            Application.Run(new PdaBrowser(parent));

            Console.WriteLine("[PDA] Application message loop exited.");

        }

    }

}

