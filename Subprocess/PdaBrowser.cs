using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace YouTubeShortsPda
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
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

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

        private const string InitialUrl = "https://www.youtube.com/shorts";
        private const string MobileUserAgent = "Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

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
            this.Text = "Europan PDA - YouTube Shorts";

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
                    
                    // Attach thread input to fix cross-process keyboard focus issues
                    uint parentPid = 0;
                    uint parentThreadId = GetWindowThreadProcessId(parentHandle, out parentPid);
                    uint childThreadId = GetCurrentThreadId();
                    if (parentThreadId != 0 && childThreadId != 0 && parentThreadId != childThreadId)
                    {
                        AttachThreadInput(childThreadId, parentThreadId, true);
                        Console.WriteLine("[PDA] Attached thread input to parent window thread.");
                    }

                    // Start monitoring parent process to exit if the game crashes
                    if (parentPid != 0)
                    {
                        StartParentProcessMonitor(parentPid);
                    }
                    
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

        private void StartParentProcessMonitor(uint parentPid)
        {
            Task.Run(async () =>
            {
                try
                {
                    var parentProcess = System.Diagnostics.Process.GetProcessById((int)parentPid);
                    while (parentProcess != null && !parentProcess.HasExited)
                    {
                        await Task.Delay(1000);
                    }
                }
                catch
                {
                    // Parent process is already dead or inaccessible
                }
                finally
                {
                    Console.WriteLine("[PDA] Parent process exited. Shutting down browser...");
                    Application.Exit();
                }
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (ledTimer != null)
                {
                    ledTimer.Stop();
                    ledTimer.Dispose();
                }
                if (webView != null)
                {
                    webView.Dispose();
                }
            }
            base.Dispose(disposing);
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
                    Action = () => { if (webView != null) webView.Source = new Uri("https://accounts.google.com/ServiceLogin?service=youtube"); }
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
                string pdaDataDir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "YouTubeShortsPDA");
                System.IO.Directory.CreateDirectory(pdaDataDir);
                string logPath = System.IO.Path.Combine(pdaDataDir, "pda_browser.log");
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
                string localAppDir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "YouTubeShortsPDA", "WebViewData");
                Log("[PDA] Creating WebView2 environment in " + localAppDir);
                var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required --disable-background-timer-throttling --disable-backgrounding-occluded-windows --disable-features=CalculateWindowOcclusion");
                var env = await CoreWebView2Environment.CreateAsync(null, localAppDir, options);
                Log("[PDA] WebView2 environment created. Initializing control...");
                await webView.EnsureCoreWebView2Async(env);
                Log("[PDA] WebView2 control initialized successfully.");

                // Set user agent to a mobile agent for vertical layout
                webView.CoreWebView2.Settings.UserAgent = MobileUserAgent;
                webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;

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

                // Intercept Beforeunload dialogs
                webView.CoreWebView2.ScriptDialogOpening += (s, e) =>
                {
                    try
                    {
                        Log(string.Format("[PDA] Script dialog opening: {0} (Message: {1})", e.Kind, e.Message));
                        if (e.Kind == CoreWebView2ScriptDialogKind.Beforeunload)
                        {
                            if (shouldBlockUnload)
                            {
                                shouldBlockUnload = false;
                                isLoading = false;
                                this.Invalidate(new Rectangle(430, 10, 100, 50));
                                Log("[PDA] Programmatically cancelled Beforeunload dialog to block redirect.");
                            }
                            else
                            {
                                e.Accept();
                                Log("[PDA] Allowed Beforeunload dialog for navigation.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("[PDA Error] ScriptDialogOpening handler failed: " + ex.ToString());
                    }
                };

                // Handle new window requests
                webView.CoreWebView2.NewWindowRequested += async (s, e) =>
                {
                    var deferral = e.GetDeferral();
                    
                    if (e.Uri == null)
                    {
                        Log("[PDA] New window requested with null URI.");
                        e.Handled = true;
                        deferral.Complete();
                        return;
                    }

                    string uri = e.Uri.ToString();
                    Log("[PDA] New window requested to: " + uri);
                    
                    string lowerUri = uri.ToLower();
                    bool isAppStoreOrDownload = lowerUri.Contains("apps.apple.com") || 
                                                lowerUri.Contains("play.google.com") || 
                                                lowerUri.Contains("itunes.apple.com") || 
                                                lowerUri.Contains("onelink.me") || 
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
                    
                    this.Enabled = false;
                    popupForm.FormClosed += (sender, args) =>
                    {
                        this.Enabled = true;
                    };

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
                        popupForm.Show(this);
                        await popupWebView.EnsureCoreWebView2Async(env);

                        popupWebView.CoreWebView2.Settings.UserAgent = MobileUserAgent;
                        popupWebView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                        popupWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                        e.NewWindow = popupWebView.CoreWebView2;
                        e.Handled = true;

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

                // Inject CSS/JS scripts to style mobile YouTube Shorts, disable app popups, and support drag-to-scroll
                string script = @"
                    (function() {
                        // Helper to check for app store redirection links
                        function isAppStoreUrl(url) {
                            if (!url) return false;
                            var lowerUrl = String(url).toLowerCase();
                            return lowerUrl.indexOf('apps.apple.com') >= 0 || 
                                   lowerUrl.indexOf('play.google.com') >= 0 || 
                                   lowerUrl.indexOf('itunes.apple.com') >= 0 || 
                                   lowerUrl.indexOf('onelink.me') >= 0 || 
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

                        // Override Location setters
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

                        // Capture clicks on app store elements
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

                        // Prevent focus/visibility blur pauses by YouTube
                        try {
                            Object.defineProperty(Document.prototype, 'visibilityState', {
                                get: function() { return 'visible'; },
                                configurable: true
                            });
                            Object.defineProperty(Document.prototype, 'hidden', {
                                get: function() { return false; },
                                configurable: true
                            });
                            Object.defineProperty(Document.prototype, 'hasFocus', {
                                value: function() { return true; },
                                writable: true,
                                configurable: true
                            });

                            var originalAddEventListener = window.addEventListener;
                            window.addEventListener = function(type, listener, options) {
                                if (type === 'blur' || type === 'focusout' || type === 'visibilitychange') {
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

                        // Override video play/pause
                        try {
                            var originalPlay = HTMLVideoElement.prototype.play;
                            HTMLVideoElement.prototype.play = function() {
                                console.log('VIDEO PLAY CALLED');
                                return originalPlay.apply(this, arguments);
                            };

                            var originalPause = HTMLVideoElement.prototype.pause;
                            HTMLVideoElement.prototype.pause = function() {
                                var stack = new Error().stack || '';
                                console.log('VIDEO PAUSE CALLED | Stack: ' + stack.substring(0, 250));
                                var stackLower = stack.toLowerCase();
                                
                                var isFocusBlurPause = stackLower.indexOf('blur') >= 0 || 
                                                       stackLower.indexOf('focus') >= 0 || 
                                                       stackLower.indexOf('visibility') >= 0;
                                
                                if (isFocusBlurPause) {
                                    console.log('BLOCKED focus/blur pause()');
                                    return;
                                }
                                
                                return originalPause.apply(this, arguments);
                            };
                        } catch(e) {
                            console.error('Video prototype hook failed: ' + e);
                        }

                        window.addEventListener('beforeunload', function(e) {
                            e.preventDefault();
                            e.returnValue = 'Block';
                            return 'Block';
                        });

                        // Drag-to-scroll navigation helper
                        let isDragging = false;
                        let startY = 0;
                        let scrollTop = 0;
                        let scrollTarget = null;
                        
                        function getScrollParent(node) {
                            if (node == null) return null;
                            if (node === document.body || node === document.documentElement) return window;
                            
                            // Check if the element is scrollable
                            if (node.scrollHeight > node.clientHeight) {
                                var style = window.getComputedStyle(node);
                                var overflowY = style.overflowY || style.overflow;
                                if (overflowY === 'auto' || overflowY === 'scroll' || overflowY === 'overlay') {
                                    return node;
                                }
                            }
                            return getScrollParent(node.parentNode);
                        }

                        window.addEventListener('mousedown', function(e) {
                            var tag = e.target.tagName.toLowerCase();
                            if (tag === 'button' || tag === 'a' || tag === 'input' || tag === 'svg' || tag === 'path' ||
                                e.target.closest('button') || e.target.closest('a') || e.target.closest('input')) {
                                return;
                            }
                            
                            scrollTarget = getScrollParent(e.target) || window;
                            isDragging = true;
                            startY = e.clientY;
                            
                            if (scrollTarget === window) {
                                scrollTop = window.pageYOffset || document.documentElement.scrollTop;
                            } else {
                                scrollTop = scrollTarget.scrollTop;
                            }
                            document.body.style.cursor = 'grabbing';
                        }, true);

                        window.addEventListener('mousemove', function(e) {
                            if (!isDragging || !scrollTarget) return;
                            e.preventDefault();
                            const y = e.clientY;
                            const walk = (y - startY) * 1.5;
                            
                            if (scrollTarget === window) {
                                window.scrollTo(0, scrollTop - walk);
                            } else {
                                scrollTarget.scrollTop = scrollTop - walk;
                            }
                        }, true);

                        window.addEventListener('mouseup', function() {
                            if (isDragging) {
                                isDragging = false;
                                document.body.style.cursor = 'default';
                            }
                        }, true);

                        window.addEventListener('mouseleave', function() {
                            if (isDragging) {
                                isDragging = false;
                                document.body.style.cursor = 'default';
                            }
                        }, true);

                        // Forward logs
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

                        // Inject custom CSS styling to make YouTube mobile layout fit the PDA window perfectly
                        function inject() {
                            var isLogin = window.location.href.indexOf('accounts.google.com') >= 0;
                            var style = document.createElement('style');
                            if (isLogin) {
                                style.innerHTML = `
                                    html, body {
                                        background-color: #000000 !important;
                                        background: #000000 !important;
                                    }
                                `;
                            } else {
                                style.innerHTML = `
                                    /* Hide YouTube Mobile Navigation Header, Pivot Bar, App Store Banners, Promos */
                                    ytm-header-bar, ytm-pivot-bar-renderer, ytm-app-header-host,
                                    .mobile-app-banner, ytm-promosheet, ytm-bottom-sheet-renderer,
                                    a[href*=""play.google.com""], a[href*=""apps.apple.com""],
                                    .ytm-app-promo, .ytm-open-app-button,
                                    .shorts-mobile-header, .shorts-header-container {
                                        display: none !important;
                                        width: 0 !important;
                                        height: 0 !important;
                                        visibility: hidden !important;
                                        opacity: 0 !important;
                                        pointer-events: none !important;
                                    }
                                    
                                    /* Force background black for all components */
                                    html, body, #app, ytm-app, ytm-shorts, .shorts-container {
                                        background-color: #000000 !important;
                                        background: #000000 !important;
                                        width: 490px !important;
                                        height: 700px !important;
                                        max-width: 490px !important;
                                        max-height: 700px !important;
                                        margin: 0 auto !important;
                                        padding: 0 !important;
                                        position: relative !important;
                                        overflow: hidden !important;
                                    }

                                    /* Ensure video fills the entire viewport */
                                    video, .video-stream, .html5-main-video {
                                        width: 100% !important;
                                        height: 100% !important;
                                        object-fit: contain !important;
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

                        // Auto-play routine and auto-close app download sheets
                        setInterval(function() {
                            // Auto-play is handled natively by YouTube and Chrome autoplay policy overrides

                            // Dismiss common popups and bottom sheets asking to install the app
                            var dismissButtons = document.querySelectorAll([
                                '.ytm-promosheet-cancel-button',
                                '.ytm-bottom-sheet-close-button',
                                'button[aria-label=""No thanks""]',
                                'button[aria-label=""Not now""]',
                                'button[aria-label=""Cancel""]',
                                '.close-button',
                                '.dismiss-button'
                            ].join(','));
                            
                            dismissButtons.forEach(function(btn) {
                                if (btn && typeof btn.click === 'function') {
                                    console.log('AUTO-CLICKING YouTube close button');
                                    btn.click();
                                }
                            });

                            // Remove overlay promo sheets directly if they exist
                            var sheets = document.querySelectorAll('ytm-promosheet, ytm-bottom-sheet-renderer, .ytm-app-promo-banner');
                            sheets.forEach(function(sheet) {
                                if (sheet) {
                                    sheet.remove();
                                }
                            });
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
                    
                    // Domain whitelist check for security / anti-phishing
                    bool isTrustedDomain = lowerUri.StartsWith("about:") || 
                                           lowerUri.Contains("youtube.com") || 
                                           lowerUri.Contains("google.com") || 
                                           lowerUri.Contains("gstatic.com") || 
                                           lowerUri.Contains("ggpht.com") || 
                                           lowerUri.Contains("ytimg.com");

                    if (!isTrustedDomain)
                    {
                        e.Cancel = true;
                        Log("[PDA] Blocked untrusted domain navigation: " + uri);
                        return;
                    }
                    
                    bool isAppStoreOrDownload = lowerUri.Contains("apps.apple.com") || 
                                                lowerUri.Contains("play.google.com") || 
                                                lowerUri.Contains("itunes.apple.com") || 
                                                lowerUri.Contains("onelink.me") || 
                                                lowerUri.Contains("apple.co");
                                                
                    bool isCustomProtocol = !lowerUri.StartsWith("http://") && 
                                            !lowerUri.StartsWith("https://") && 
                                            !lowerUri.StartsWith("about:");
                    
                    if (isAppStoreOrDownload || isCustomProtocol || lowerUri.StartsWith("itms-apps") || lowerUri.Contains("market://"))
                    {
                        if (isCustomProtocol || lowerUri.StartsWith("itms-apps"))
                        {
                            e.Cancel = true;
                            Log("[PDA] Instantly blocked custom protocol: " + uri);
                        }
                        else
                        {
                            shouldBlockUnload = true;
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
            using (LinearGradientBrush brush = new LinearGradientBrush(this.ClientRectangle, 
                Color.FromArgb(40, 44, 52), Color.FromArgb(20, 22, 26), 45F))
            {
                g.FillRectangle(brush, this.ClientRectangle);
            }

            using (Pen pen = new Pen(Color.FromArgb(12, 13, 15), 3))
            {
                g.DrawRectangle(pen, 23, 63, 494, 704);
            }

            using (Pen pen = new Pen(Color.FromArgb(60, 65, 75), 2))
            {
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
            Color ledColor = ledState ? Color.FromArgb(255, 0, 50) : Color.FromArgb(80, 0, 10); // Red LED for YouTube
            using (SolidBrush brush = new SolidBrush(ledColor))
            {
                g.FillEllipse(brush, 35, 23, 14, 14);
            }
            if (ledState)
            {
                using (PathGradientBrush rgb = CreateRadialBrush(new PointF(42, 30), 12, Color.FromArgb(100, 255, 0, 50), Color.Transparent))
                {
                    ColorBlend cb = new ColorBlend(3);
                    cb.Colors = new Color[] { Color.FromArgb(100, 255, 0, 50), Color.FromArgb(30, 255, 0, 50), Color.Transparent };
                    cb.Positions = new float[] { 0.0f, 0.4f, 1.0f };
                    rgb.InterpolationColors = cb;
                    g.FillEllipse(rgb, 27, 15, 30, 30);
                }
            }
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(200, 30, 30)))
            {
                g.FillEllipse(brush, 60, 23, 8, 8);
            }

            // --- SYSTEM TEXT ---
            using (Font font = new Font("Courier New", 9, FontStyle.Bold))
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(130, 140, 150)))
            {
                g.DrawString("YT-OS v1.00", font, brush, 80, 21);
            }

            // Loading status text
            if (isLoading)
            {
                using (Font font = new Font("Courier New", 8, FontStyle.Bold))
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(255, 50, 50)))
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

                using (Font font = new Font("Courier New", 9, FontStyle.Bold))
                using (SolidBrush brush = new SolidBrush(button.IsHovered ? button.GlowColor : Color.FromArgb(180, 190, 200)))
                {
                    SizeF textSize = g.MeasureString(button.Label, font);
                    float xText = button.Bounds.X + (button.Bounds.Width - textSize.Width) / 2;
                    float yText = button.Bounds.Y + (button.Bounds.Height - textSize.Height) / 2;
                    g.DrawString(button.Label, font, brush, xText, yText);
                }

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
