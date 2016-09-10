using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Runtime.InteropServices;
using System.Drawing;
using System.IO;
using System.Windows.Threading;
using System.Diagnostics;
using System.Media;

namespace LiverColle
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        };
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        };

        [DllImport("user32.dll")]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, int uFlags);
        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hWnd, ref RECT lpRect);
        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, ref RECT lpRect);
        [DllImport("user32.dll")]
        extern static bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
        [DllImport("user32.dll")]
        public static extern int PostMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);


        const int WM_LBUTTONDOWN = 0x0201;
        const int WM_LBUTTONUP = 0x0202;

        class MouseAction
        {
            public bool wait = false;
            public int x;
            public int y;
            public int waitDuration;
            public Stages availStage;
            public MouseAction(bool wait, int x, int y, int waitDuration, Stages availStage)
            {
                this.x = x;
                this.y = y;
                this.wait = wait;
                this.waitDuration = waitDuration;
                this.availStage = availStage;
            }
        };

        class Condition
        {
            public int taiha = 0;
            public int cyuha = 0;
            public int syouha = 0;
            public int others = 0;
            public int red = 0;
            public int yellow = 0;
        };

        string WindowName = "poi";
        string ClassName = "Chrome_WidgetWin_1";
        IntPtr hWnd = new IntPtr();
        int standardClientWidth = 1284;
        int standardClientHeight = 586;
        public static RECT selection;
        public static POINT clickPos;
        DispatcherTimer viewTimer = new DispatcherTimer();
        DispatcherTimer clickTimer = new DispatcherTimer();
        WriteableBitmap origCapture;
        int distanceThresh = 10;
        int stageDuration = 0;
        Queue<MouseAction> MouseActionQueue = new Queue<MouseAction>();
        int waitTicks = 0;
        bool kiAvail = false;
        bool needSupply = false;
        bool played = false;
        bool reachedUpperBound = false;
        SoundPlayer player = new SoundPlayer(Properties.Resources.Notification);
        Condition currentCondition = new Condition();
        int shingekiKaisu = 0;
        int lastPosX = 100;
        int lastPosY = 100;

        enum Stages
        {
            Unknown, GameStart, EnseiKitou, Bokou, SyutsugekiSentaku, ChinsyuhuKaiiki, ChinsyuhuKaiikiEO,
            SyutsugekiSyousai, KantaiSentaku, SenkaHoukoku, ShingekiTettai, Rashinban, RidatsuHantei,
            KansenSentaku, All, JinkeiSentaku, Kousho, ShizaiTounyu
        };
        enum Actions
        {
            Syutsugeki, SyutsugekiSentaku, SyutsugekiEO, SelectEO, SyutsugekiKettei, SyutsugekiKaishi,
            EnseiKitou, Kantai1Sentaku, Kantai2Sentaku, Kantai3Sentaku, Kantai4Sentaku, Reload, GameStart,
            WaitShort, YasenTotsunyu, SyutsugekiSyoumenkaiiki, Shingeki, WaitNormal, Click, Hokyu,
            Kantai3SentakuHokyu, Kantai2SentakuHokyu, Kantai1SentakuHokyu, Kantai4SentakuHokyu, ZenHokyu, BokouModori,
            TsuigekiSezu, Tanoujin, Tettai, Kaihatsu, KaihatsuKaishi
        };
        enum Schemes
        {
            TankanKira, Taisen, Kaihatsu, SenseiTaisen
        }

        Stages currentStage, lastStage;
        Schemes currentScheme;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            DetectWindow(this, new RoutedEventArgs());

            viewTimer.Interval = new TimeSpan(0, 0, 0, 1);
            viewTimer.Tick += ViewTimerHandler;

            clickTimer.Interval = new TimeSpan(0, 0, 0, 0, 200);
            clickTimer.Tick += MouseControl;
            clickTimer.Start();


        }

        private void MouseControl(object sender, EventArgs e)
        {
            if (waitTicks > 0)
            {
                waitTicks -= 1;
                return;
            }
            if (MouseActionQueue.Count != 0)
            {
                MouseAction act = MouseActionQueue.Dequeue();
                if (act.wait)
                {
                    waitTicks = act.waitDuration;
                }
                else
                {
                    if (currentStage == act.availStage || act.availStage == Stages.All)
                        MouseClick(act.x, act.y);
                }
            }
        }

        private void ViewTimerHandler(object sender, EventArgs e)
        {
            RefreshView();
        }

        int GetDistance(ulong a, ulong b)
        {
            int dist = 0;
            ulong c = a ^ b;
            for (int i = 0; i < 64; i++)
            {
                if ((c & 1) == 1)
                {
                    dist++;
                }
                c = c >> 1;
            }
            return dist;
        }

        ulong GetHash(BitmapSource bmp)
        {
            int width = bmp.PixelWidth;
            int height = bmp.PixelHeight;
            BitmapSource tbmp = ResizeImage(bmp, 9, 8);
            int stride = tbmp.PixelWidth * (bmp.Format.BitsPerPixel / 8);
            byte[] pixelData = new byte[stride * tbmp.PixelHeight];
            tbmp.CopyPixels(pixelData, stride, 0);
            ulong hash = 0;
            for (int i = 0; i < tbmp.PixelHeight; i++)
            {
                for (int j = 0; j < tbmp.PixelWidth - 1; j++)
                {
                    int bl = 0; int br = 0;
                    for (int k = 0; k < bmp.Format.BitsPerPixel / 8; k++)
                    {
                        bl += (int)pixelData[i * stride + j * (bmp.Format.BitsPerPixel / 8) + k];
                    }
                    for (int k = 0; k < bmp.Format.BitsPerPixel / 8; k++)
                    {
                        br += (int)pixelData[i * stride + (j + 1) * (bmp.Format.BitsPerPixel / 8) + k];
                    }
                    if (bl > br)
                    {
                        hash += 1;
                    }
                    hash = hash << 1;
                }
            }
            return hash;
        }

        BitmapSource ResizeImage(BitmapSource bmp, double px, double py)
        {
            double scaleX = px / bmp.PixelWidth;
            double scaleY = py / bmp.PixelHeight;
            ScaleTransform trans = new ScaleTransform(scaleX, scaleY);
            TransformedBitmap tbmp = new TransformedBitmap(bmp, trans);
            return tbmp;
        }

        BitmapSource CropImage(BitmapSource bmp, int left, int top, int right, int bottom)
        {
            return new CroppedBitmap(bmp, new Int32Rect(left, top, right - left + 1, bottom - top + 1));
        }


        async Task RefreshView()
        {
            Stopwatch watch = Stopwatch.StartNew();
            if (StateCheck())
            {

                Bitmap scrshot = new Bitmap(standardClientWidth, standardClientHeight);
                POINT pt = new POINT();
                ClientToScreen(hWnd, ref pt);
                using (Graphics g = Graphics.FromImage(scrshot))
                {
                    g.CopyFromScreen(pt.x, pt.y, 0, 0, new System.Drawing.Size(standardClientWidth, standardClientHeight));
                }
                using (MemoryStream ms = new MemoryStream())
                {
                    BitmapImage bmp = new BitmapImage();

                    scrshot.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                    ms.Position = 0;
                    bmp.BeginInit();
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    origCapture = new WriteableBitmap(bmp);
                }

                lastStage = currentStage;

                DetermineStage();

                RefreshDuration();

                GetClues();

                currentScheme = (Schemes)SchemeSelector.SelectedIndex;

                ConditionCheck();

                ExecuteScheme();

            }
            else
            {
                StopView(this, new RoutedEventArgs());
            }
            watch.Stop();
            ViewCounter.Content = watch.ElapsedMilliseconds.ToString();
        }

        void ConditionCheck()
        {
            currentCondition.taiha = currentCondition.syouha = currentCondition.cyuha = currentCondition.others =
                currentCondition.red = currentCondition.yellow = 0;

            System.Windows.Media.Color[] c = new System.Windows.Media.Color[12];
            int[] x = { 886, 886, 886, 886, 886, 886, 1008, 1008, 1008, 1008, 1008, 1008 };
            int[] y = { 265, 307, 350, 391, 433, 475, 248, 290, 332, 374, 416, 458 };

            GetViewColors(ref c, x, y);
            for (int i = 0; i < 12; i++)
            {
                UpdateCondition(c[i]);
            }

            TaihaNumberDisplay.Content = currentCondition.taiha.ToString();
            CyuNumberDisplay.Content = currentCondition.cyuha.ToString();
            SyouhaNumberDisplay.Content = currentCondition.syouha.ToString();
            HokaNumberDisplay.Content = currentCondition.others.ToString();
            YellowMoraleDisplay.Content = currentCondition.yellow.ToString();
            RedMoraleDisplay.Content = currentCondition.red.ToString();


            if (StopConditionSelector.SelectedIndex == 0)
            {
                if ((currentCondition.syouha + currentCondition.cyuha + currentCondition.taiha) > 0)
                {
                    StopView(this, new RoutedEventArgs());
                    player.Load();
                    player.Play();
                }
            }
            else if (StopConditionSelector.SelectedIndex == 1)
            {
                if ((currentCondition.cyuha + currentCondition.taiha) > 0)
                {
                    StopView(this, new RoutedEventArgs());
                    player.Load();
                    player.Play();
                }
            }
            else if (StopConditionSelector.SelectedIndex == 2)
            {
                if ((currentCondition.taiha) > 0)
                {
                    StopView(this, new RoutedEventArgs());
                    player.Load();
                    player.Play();
                }
            }
            if (StopConditionMoraleSelector.SelectedIndex == 0)
            {
                if ((currentCondition.red + currentCondition.yellow) > 0)
                {
                    StopView(this, new RoutedEventArgs());
                    player.Load();
                    player.Play();
                }
            }
            else if (StopConditionMoraleSelector.SelectedIndex == 1)
            {
                if ((currentCondition.red) > 0)
                {
                    StopView(this, new RoutedEventArgs());
                    player.Load();
                    player.Play();
                }
            }
            else if (StopConditionMoraleSelector.SelectedIndex == 2)
            {
                ;
            }
        }

        void UpdateCondition(System.Windows.Media.Color c)
        {
            if (c == System.Windows.Media.Color.FromArgb(255, 76, 175, 80))
            {
                currentCondition.others++;
            }
            else if (c == System.Windows.Media.Color.FromArgb(255, 255, 185, 15))
            {
                currentCondition.syouha++;
            }
            else if (c == System.Windows.Media.Color.FromArgb(255, 229, 28, 35))
            {
                currentCondition.taiha++;
            }
            else if (c == System.Windows.Media.Color.FromArgb(255, 238, 118, 0))
            {
                currentCondition.cyuha++;
            }
            else if (c == System.Windows.Media.Color.FromArgb(255, 243, 123, 29))
            {
                currentCondition.yellow++;
            }
            else if (c == System.Windows.Media.Color.FromArgb(255, 221, 81, 74))
            {
                currentCondition.red++;
            }
        }

        void ExecuteScheme()
        {
            switch (currentScheme)
            {
                case Schemes.SenseiTaisen:
                    if (currentStage == Stages.EnseiKitou)
                    {
                        TakeAction(Actions.EnseiKitou);
                    }
                    else if (currentStage == Stages.Bokou)
                    {
                        if (!needSupply)
                        {
                            TakeAction(Actions.Syutsugeki);
                        }
                        else
                        {
                            TakeAction(Actions.Hokyu);
                        }
                    }
                    else if (currentStage == Stages.SyutsugekiSentaku)
                    {
                        TakeAction(Actions.SyutsugekiSentaku);
                    }
                    else if (currentStage == Stages.ChinsyuhuKaiiki)
                    {
                        TakeAction(Actions.SyutsugekiEO);
                    }
                    else if (currentStage == Stages.ChinsyuhuKaiikiEO)
                    {
                        TakeAction(Actions.SelectEO);
                    }
                    else if (currentStage == Stages.SyutsugekiSyousai)
                    {
                        TakeAction(Actions.SyutsugekiKettei);
                    }
                    else if (currentStage == Stages.KantaiSentaku)
                    {
                        if (FleetNumberSelector.SelectedIndex == 0)
                        {
                            TakeAction(Actions.Kantai1Sentaku);
                        }
                        else if (FleetNumberSelector.SelectedIndex == 1)
                        {
                            TakeAction(Actions.Kantai2Sentaku);
                        }
                        if (FleetNumberSelector.SelectedIndex == 2)
                        {
                            TakeAction(Actions.Kantai3Sentaku);
                        }
                        else if (FleetNumberSelector.SelectedIndex == 3)
                        {
                            TakeAction(Actions.Kantai4Sentaku);
                        }
                        TakeAction(Actions.WaitShort);
                        TakeAction(Actions.SyutsugekiKaishi);
                    }
                    else if (currentStage == Stages.GameStart)
                    {
                        TakeAction(Actions.GameStart);
                    }
                    else if (currentStage == Stages.RidatsuHantei)
                    {
                        TakeAction(Actions.TsuigekiSezu);
                    }
                    else if (currentStage == Stages.JinkeiSentaku)
                    {
                        TakeAction(Actions.Tanoujin);
                    }
                    else if (currentStage == Stages.SenkaHoukoku)
                    {
                        TakeAction(Actions.Click);
                    }
                    else if (currentStage == Stages.Rashinban)
                    {
                        TakeAction(Actions.Click);
                    }
                    else if (currentStage == Stages.ShingekiTettai)
                    {
                        TakeAction(Actions.Tettai);
                    }
                    else if (currentStage == Stages.Unknown)
                    {
                        if (kiAvail)
                        {
                            TakeAction(Actions.Click);
                        }
                    }
                    else if (currentStage == Stages.KansenSentaku)
                    {
                        if (FleetNumberSelector.SelectedIndex == 0)
                        {
                            TakeAction(Actions.Kantai1SentakuHokyu);
                        }
                        else if (FleetNumberSelector.SelectedIndex == 1)
                        {
                            TakeAction(Actions.Kantai2SentakuHokyu);
                        }
                        if (FleetNumberSelector.SelectedIndex == 2)
                        {
                            TakeAction(Actions.Kantai3SentakuHokyu);
                        }
                        else if (FleetNumberSelector.SelectedIndex == 3)
                        {
                            TakeAction(Actions.Kantai4SentakuHokyu);
                        }
                        TakeAction(Actions.WaitShort);
                        TakeAction(Actions.ZenHokyu);
                        TakeAction(Actions.WaitNormal);
                        TakeAction(Actions.BokouModori);
                    }
                    break;
                case Schemes.Kaihatsu:
                    if (currentStage == Stages.Kousho)
                    {
                        TakeAction(Actions.Kaihatsu);
                    }
                    else if (currentStage == Stages.ShizaiTounyu)
                    {
                        TakeAction(Actions.KaihatsuKaishi);
                    }
                    else if (currentStage == Stages.Unknown)
                    {
                        if (kiAvail)
                        {
                            TakeAction(Actions.Click);
                        }
                    }
                    break;
                case Schemes.Taisen:
                    if (currentStage == Stages.EnseiKitou)
                    {
                        TakeAction(Actions.EnseiKitou);
                    }
                    else if (currentStage == Stages.Bokou)
                    {
                        shingekiKaisu = 0;
                        if (!needSupply)
                        {
                            TakeAction(Actions.Syutsugeki);
                        }
                        else
                        {
                            TakeAction(Actions.Hokyu);
                        }
                    }
                    else if (currentStage == Stages.SyutsugekiSentaku)
                    {
                        TakeAction(Actions.SyutsugekiSentaku);
                    }
                    else if (currentStage == Stages.ChinsyuhuKaiiki)
                    {
                        TakeAction(Actions.SyutsugekiEO);
                    }
                    else if (currentStage == Stages.ChinsyuhuKaiikiEO)
                    {
                        TakeAction(Actions.SelectEO);
                    }
                    else if (currentStage == Stages.SyutsugekiSyousai)
                    {
                        TakeAction(Actions.SyutsugekiKettei);
                    }
                    else if (currentStage == Stages.KantaiSentaku)
                    {
                        if (FleetNumberSelector.SelectedIndex == 0)
                        {
                            TakeAction(Actions.Kantai1Sentaku);
                        }
                        else if (FleetNumberSelector.SelectedIndex == 1)
                        {
                            TakeAction(Actions.Kantai2Sentaku);
                        }
                        if (FleetNumberSelector.SelectedIndex == 2)
                        {
                            TakeAction(Actions.Kantai3Sentaku);
                        }
                        else if (FleetNumberSelector.SelectedIndex == 3)
                        {
                            TakeAction(Actions.Kantai4Sentaku);
                        }
                        TakeAction(Actions.WaitShort);
                        TakeAction(Actions.SyutsugekiKaishi);
                    }
                    else if (currentStage == Stages.GameStart)
                    {
                        TakeAction(Actions.GameStart);
                    }
                    else if (currentStage == Stages.RidatsuHantei)
                    {
                        TakeAction(Actions.TsuigekiSezu);
                    }
                    else if (currentStage == Stages.JinkeiSentaku)
                    {
                        TakeAction(Actions.Tanoujin);
                    }
                    else if (currentStage == Stages.SenkaHoukoku)
                    {
                        TakeAction(Actions.Click);
                    }
                    else if (currentStage == Stages.Rashinban)
                    {
                        TakeAction(Actions.Click);
                    }
                    else if (currentStage == Stages.ShingekiTettai)
                    {
                        if (shingekiKaisu < 2)
                        {
                            TakeAction(Actions.Shingeki);
                            shingekiKaisu++;
                        }
                        else
                        {
                            TakeAction(Actions.Tettai);
                        }

                    }
                    else if (currentStage == Stages.Unknown)
                    {
                        if (kiAvail)
                        {
                            TakeAction(Actions.Click);
                        }
                    }
                    else if (currentStage == Stages.KansenSentaku)
                    {
                        if (FleetNumberSelector.SelectedIndex == 0)
                        {
                            TakeAction(Actions.Kantai1SentakuHokyu);
                        }
                        else if (FleetNumberSelector.SelectedIndex == 1)
                        {
                            TakeAction(Actions.Kantai2SentakuHokyu);
                        }
                        if (FleetNumberSelector.SelectedIndex == 2)
                        {
                            TakeAction(Actions.Kantai3SentakuHokyu);
                        }
                        else if (FleetNumberSelector.SelectedIndex == 3)
                        {
                            TakeAction(Actions.Kantai4SentakuHokyu);
                        }
                        TakeAction(Actions.WaitShort);
                        TakeAction(Actions.ZenHokyu);
                        TakeAction(Actions.WaitNormal);
                        TakeAction(Actions.BokouModori);
                    }
                    break;
                case Schemes.TankanKira:
                    if (currentStage == Stages.EnseiKitou)
                    {
                        TakeAction(Actions.EnseiKitou);
                    }
                    else if (currentStage == Stages.Bokou)
                    {
                        if (GetDistance(GetAreaHash(995, 239, 1025, 253), 20910831178385920) < 1)
                        {
                            return;
                        }
                        if (!needSupply)
                        {
                            TakeAction(Actions.Syutsugeki);
                        }
                        else
                        {

                            TakeAction(Actions.Hokyu);
                        }
                    }
                    else if (currentStage == Stages.SyutsugekiSentaku)
                    {
                        TakeAction(Actions.SyutsugekiSentaku);
                    }
                    else if (currentStage == Stages.ChinsyuhuKaiiki)
                    {
                        TakeAction(Actions.SyutsugekiSyoumenkaiiki);
                    }
                    else if (currentStage == Stages.ChinsyuhuKaiikiEO)
                    {
                        TakeAction(Actions.SelectEO);
                    }
                    else if (currentStage == Stages.SyutsugekiSyousai)
                    {
                        TakeAction(Actions.SyutsugekiKettei);
                    }
                    else if (currentStage == Stages.KantaiSentaku)
                    {
                        if (FleetNumberSelector.SelectedIndex == 0)
                        {
                            TakeAction(Actions.Kantai1Sentaku);
                        }
                        else if (FleetNumberSelector.SelectedIndex == 1)
                        {
                            TakeAction(Actions.Kantai2Sentaku);
                        }
                        if (FleetNumberSelector.SelectedIndex == 2)
                        {
                            TakeAction(Actions.Kantai3Sentaku);
                        }
                        else if (FleetNumberSelector.SelectedIndex == 3)
                        {
                            TakeAction(Actions.Kantai4Sentaku);
                        }
                        TakeAction(Actions.WaitShort);
                        TakeAction(Actions.SyutsugekiKaishi);
                    }
                    else if (currentStage == Stages.GameStart)
                    {
                        TakeAction(Actions.GameStart);
                    }
                    else if (currentStage == Stages.RidatsuHantei)
                    {
                        TakeAction(Actions.YasenTotsunyu);
                    }
                    else if (currentStage == Stages.SenkaHoukoku)
                    {
                        TakeAction(Actions.Click);
                    }
                    else if (currentStage == Stages.Rashinban)
                    {
                        TakeAction(Actions.Click);
                    }
                    else if (currentStage == Stages.ShingekiTettai)
                    {
                        TakeAction(Actions.Shingeki);
                    }
                    else if (currentStage == Stages.Unknown)
                    {
                        if (kiAvail)
                        {
                            TakeAction(Actions.Click);
                        }
                    }
                    else if (currentStage == Stages.KansenSentaku)
                    {
                        if (FleetNumberSelector.SelectedIndex == 0)
                        {
                            TakeAction(Actions.Kantai1SentakuHokyu);
                        }
                        else if (FleetNumberSelector.SelectedIndex == 1)
                        {
                            TakeAction(Actions.Kantai2SentakuHokyu);
                        }
                        if (FleetNumberSelector.SelectedIndex == 2)
                        {
                            TakeAction(Actions.Kantai3SentakuHokyu);
                        }
                        else if (FleetNumberSelector.SelectedIndex == 3)
                        {
                            TakeAction(Actions.Kantai4SentakuHokyu);
                        }
                        TakeAction(Actions.WaitShort);
                        TakeAction(Actions.ZenHokyu);
                        TakeAction(Actions.WaitNormal);
                        TakeAction(Actions.BokouModori);
                    }
                    break;
            }
        }

        void GetViewColors(ref System.Windows.Media.Color[] cs, int[] x, int[] y)
        {
            if (StateCheck())
            {
                System.Windows.Media.Color c = new System.Windows.Media.Color();
                int width = origCapture.PixelWidth;
                int height = origCapture.PixelHeight;
                int stride = origCapture.PixelWidth * (origCapture.Format.BitsPerPixel / 8);
                byte[] pixelData = new byte[stride * origCapture.PixelHeight];
                origCapture.CopyPixels(pixelData, stride, 0);
                for (int i = 0; i < cs.Length; i++)
                {
                    c.R = pixelData[stride * y[i] + x[i] * (origCapture.Format.BitsPerPixel / 8) + 2];
                    c.G = pixelData[stride * y[i] + x[i] * (origCapture.Format.BitsPerPixel / 8) + 1];
                    c.B = pixelData[stride * y[i] + x[i] * (origCapture.Format.BitsPerPixel / 8)];
                    c.A = 255;
                    cs[i] = c;
                }

            }
        }

        System.Windows.Media.Color GetPixelColor(int x, int y)
        {
            if (StateCheck())
            {
                Bitmap scrshot = new Bitmap(standardClientWidth, standardClientHeight);
                POINT pt = new POINT();
                ClientToScreen(hWnd, ref pt);
                using (Graphics g = Graphics.FromImage(scrshot))
                {
                    g.CopyFromScreen(pt.x, pt.y, 0, 0, new System.Drawing.Size(standardClientWidth, standardClientHeight));
                }
                using (MemoryStream ms = new MemoryStream())
                {
                    BitmapImage bmp = new BitmapImage();

                    scrshot.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                    ms.Position = 0;
                    bmp.BeginInit();
                    bmp.StreamSource = ms;
                    bmp.EndInit();

                    System.Windows.Media.Color c = new System.Windows.Media.Color();
                    int width = bmp.PixelWidth;
                    int height = bmp.PixelHeight;
                    int stride = bmp.PixelWidth * (bmp.Format.BitsPerPixel / 8);
                    byte[] pixelData = new byte[stride * bmp.PixelHeight];
                    bmp.CopyPixels(pixelData, stride, 0);

                    c.R = pixelData[stride * y + x * (bmp.Format.BitsPerPixel / 8) + 2];
                    c.G = pixelData[stride * y + x * (bmp.Format.BitsPerPixel / 8) + 1];
                    c.B = pixelData[stride * y + x * (bmp.Format.BitsPerPixel / 8)];
                    c.A = 255;

                    return c;
                }
            }

            return new System.Windows.Media.Color();
        }

        void GetClues()
        {
            if (GetDistance(GetAreaHash(752, 469, 775, 492), 11074943912661989218) < distanceThresh || GetDistance(GetAreaHash(748, 463, 772, 492), 14002328584218979118) < distanceThresh)
            {
                kiAvail = true;
                KiAvailabilityDisplay.Content = "Available";
            }
            else
            {
                kiAvail = false;
                KiAvailabilityDisplay.Content = "Inavailable";
            }
            if (GetDistance(GetAreaHash(972, 239, 988, 254), 2165701932204039680) < distanceThresh)
            {
                needSupply = true;
                SupplyDisplay.Content = "Yes";
            }
            else
            {
                needSupply = false;
                SupplyDisplay.Content = "No";
            }
            if (currentStage == Stages.SyutsugekiSyousai && lastStage != Stages.SyutsugekiSyousai)
            {
                if (GetDistance(GetAreaHash(586, 343, 610, 357), 10759470980089470392) < distanceThresh)
                {
                    reachedUpperBound = true;
                    ReachedUpperBoundDisplay.Content = "Yes";
                    player.Load();
                    player.Play();
                }
                else
                {
                    reachedUpperBound = false;
                    ReachedUpperBoundDisplay.Content = "No";
                }
            }
        }

        void TakeAction(Actions action)
        {
            if (action == Actions.Syutsugeki)
            {
                MouseAction act = new MouseAction(false, 198, 300, 0, Stages.Bokou);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.SyutsugekiSentaku)
            {
                MouseAction act = new MouseAction(false, 226, 248, 0, Stages.SyutsugekiSentaku);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.SyutsugekiEO)
            {
                MouseAction act = new MouseAction(false, 715, 313, 0, Stages.ChinsyuhuKaiiki);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.SelectEO)
            {
                MouseAction act = new MouseAction(false, 444, 242, 0, Stages.ChinsyuhuKaiikiEO);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.SyutsugekiKettei)
            {
                MouseAction act = new MouseAction(false, 686, 482, 0, Stages.SyutsugekiSyousai);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.SyutsugekiKaishi)
            {
                MouseAction act = new MouseAction(false, 626, 484, 0, Stages.KantaiSentaku);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.EnseiKitou)
            {
                MouseAction act = new MouseAction(false, 198, 300, 0, Stages.EnseiKitou);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.Kantai3Sentaku)
            {
                MouseAction act = new MouseAction(false, 425, 153, 0, Stages.KantaiSentaku);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.Kantai1Sentaku)
            {
                MouseAction act = new MouseAction(false, 363, 158, 0, Stages.KantaiSentaku);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.Kantai2Sentaku)
            {
                MouseAction act = new MouseAction(false, 394, 158, 0, Stages.KantaiSentaku);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.Kantai4Sentaku)
            {
                MouseAction act = new MouseAction(false, 455, 154, 0, Stages.KantaiSentaku);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.Reload)
            {

            }
            else if (action == Actions.GameStart)
            {
                MouseAction act = new MouseAction(false, 601, 445, 0, Stages.GameStart);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.WaitShort)
            {
                MouseAction act = new MouseAction(true, 0, 0, 1, Stages.All);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.WaitNormal)
            {
                MouseAction act = new MouseAction(true, 0, 0, 4, Stages.All);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.YasenTotsunyu)
            {
                MouseAction act = new MouseAction(false, 503, 275, 0, Stages.RidatsuHantei);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.SyutsugekiSyoumenkaiiki)
            {
                MouseAction act = new MouseAction(false, 298, 236, 0, Stages.ChinsyuhuKaiiki);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.Shingeki)
            {
                MouseAction act = new MouseAction(false, 292, 278, 0, Stages.ShingekiTettai);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.Click)
            {
                MouseAction act = new MouseAction(false, 198, 300, 0, Stages.All);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.Hokyu)
            {
                MouseAction act = new MouseAction(false, 77, 261, 0, Stages.Bokou);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.Kantai3SentakuHokyu)
            {
                MouseAction act = new MouseAction(false, 209, 158, 0, Stages.KansenSentaku);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.Kantai1SentakuHokyu)
            {
                MouseAction act = new MouseAction(false, 149, 158, 0, Stages.KansenSentaku);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.Kantai2SentakuHokyu)
            {
                MouseAction act = new MouseAction(false, 178, 158, 0, Stages.KansenSentaku);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.Kantai4SentakuHokyu)
            {
                MouseAction act = new MouseAction(false, 238, 158, 0, Stages.KansenSentaku);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.ZenHokyu)
            {
                MouseAction act = new MouseAction(false, 115, 157, 0, Stages.KansenSentaku);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.BokouModori)
            {
                MouseAction act = new MouseAction(false, 44, 74, 0, Stages.KansenSentaku);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.Tanoujin)
            {
                MouseAction act = new MouseAction(false, 644, 379, 0, Stages.JinkeiSentaku);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.TsuigekiSezu)
            {
                MouseAction act = new MouseAction(false, 286, 272, 0, Stages.RidatsuHantei);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.Tettai)
            {
                MouseAction act = new MouseAction(false, 505, 279, 0, Stages.ShingekiTettai);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.Kaihatsu)
            {
                MouseAction act = new MouseAction(false, 222, 376, 0, Stages.Kousho);
                MouseActionQueue.Enqueue(act);
            }
            else if (action == Actions.KaihatsuKaishi)
            {
                MouseAction act = new MouseAction(false, 700, 477, 0, Stages.ShizaiTounyu);
                MouseActionQueue.Enqueue(act);
            }
        }

        void RefreshDuration()
        {
            if (stageDuration > 60 && !played)
            {
                player.Load();
                player.Play();
                played = true;
            }
            if (lastStage == currentStage)
            {
                stageDuration += 1;
            }
            else
            {
                stageDuration = 0;
                played = false;
            }
            StageDurationDisplay.Content = stageDuration.ToString();
        }

        void DetermineStage()
        {
            if (GetDistance(GetAreaHash(499, 417, 526, 444), 10965815146611940250) < distanceThresh)
            {
                currentStage = Stages.GameStart;
                StageDisplay.Content = "Game Start";
            }
            else if (GetDistance(GetAreaHash(501, 50, 538, 87), 10344149840484133514) < distanceThresh)
            {
                currentStage = Stages.EnseiKitou;
                StageDisplay.Content = "遠征帰投";
            }
            else if (GetDistance(GetAreaHash(166, 167, 201, 209), 6985881483528262840) < distanceThresh)
            {
                currentStage = Stages.Bokou;
                StageDisplay.Content = "母港";
            }
            else if (GetDistance(GetAreaHash(123, 111, 139, 128), 2184338540409799234) < distanceThresh)
            {
                currentStage = Stages.SyutsugekiSentaku;
                StageDisplay.Content = "出撃選択";
            }
            else if (GetDistance(GetAreaHash(626, 280, 654, 308), 10020985124551445714) < distanceThresh)
            {
                currentStage = Stages.ChinsyuhuKaiiki;
                StageDisplay.Content = "鎮守府海域";
            }
            else if (GetDistance(GetAreaHash(619, 232, 654, 258), 5640413726515483332) < distanceThresh)
            {
                currentStage = Stages.ChinsyuhuKaiikiEO;
                StageDisplay.Content = "鎮守府海域EO";
            }
            else if (GetDistance(GetAreaHash(582, 108, 600, 128), 13947740766061487400) < distanceThresh)
            {
                currentStage = Stages.SyutsugekiSyousai;
                StageDisplay.Content = "出撃詳細";
            }
            else if (GetDistance(GetAreaHash(343, 112, 361, 129), 6655295866747030828) < distanceThresh)
            {
                currentStage = Stages.KantaiSentaku;
                StageDisplay.Content = "艦隊選択";
            }
            else if (GetDistance(GetAreaHash(36, 67, 62, 89), 5214722010633352754) < distanceThresh)
            {
                currentStage = Stages.SenkaHoukoku;
                StageDisplay.Content = "戦果報告";
            }
            else if (GetDistance(GetAreaHash(244, 239, 294, 298), 10137372613956798326) < distanceThresh)
            {
                currentStage = Stages.ShingekiTettai;
                StageDisplay.Content = "進撃撤退";
            }
            else if (GetDistance(GetAreaHash(282, 254, 320, 304), 14397161134667712486) < distanceThresh)
            {
                currentStage = Stages.Rashinban;
                StageDisplay.Content = "羅針盤";
            }
            else if (GetDistance(GetAreaHash(38, 66, 63, 91), 6653010978507898000) < distanceThresh)
            {
                currentStage = Stages.RidatsuHantei;
                StageDisplay.Content = "離脱判定";
            }
            else if (GetDistance(GetAreaHash(128, 113, 145, 127), 6077093350316737602) < distanceThresh)
            {
                currentStage = Stages.KansenSentaku;
                StageDisplay.Content = "艦船選択";
            }
            else if (GetDistance(GetAreaHash(622, 373, 640, 391), 5570210965256328720) < distanceThresh)
            {
                currentStage = Stages.JinkeiSentaku;
                StageDisplay.Content = "陣形選択";
            }
            else if (GetDistance(GetAreaHash(369, 109, 468, 128), 8468327643040680852) < distanceThresh)
            {
                currentStage = Stages.Kousho;
                StageDisplay.Content = "工廠";
            }
            else if (GetDistance(GetAreaHash(283, 108, 373, 129), 1921478029871601490) < distanceThresh)
            {
                currentStage = Stages.ShizaiTounyu;
                StageDisplay.Content = "資材投入";
            }
            else
            {
                currentStage = Stages.Unknown;
                StageDisplay.Content = "Unknown";
            }
        }

        ulong GetAreaHash(int left, int top, int right, int bottom)
        {
            ulong hash;
            if (StateCheck())
            {
                BitmapSource cbmp = CropImage(origCapture, left, top, right, bottom);
                hash = GetHash(cbmp);
                return hash;
            }
            return 0;
        }


        async Task MouseClick(int xpos, int ypos)
        {
            PostMessage(hWnd, WM_LBUTTONDOWN, 0, (ypos << 16) + xpos);
            await Task.Delay(80);
            PostMessage(hWnd, WM_LBUTTONUP, 0, (ypos << 16) + xpos);
        }

        void HaltExecution()
        {
            Veil.Visibility = Visibility.Visible;
            ControlPanel.IsEnabled = false;
        }
        void BeginExecution()
        {
            Veil.Visibility = Visibility.Hidden;
            ControlPanel.IsEnabled = true;
        }

        private void LoadSelection(object sender, RoutedEventArgs e)
        {
            CropHelper cropHelper = new CropHelper();
            cropHelper.Closed += UpdateSelection;
            cropHelper.Show();
        }

        private void UpdateSelection(object sender, EventArgs e)
        {
            hWnd = FindWindow(ClassName, WindowName);
            if (hWnd == IntPtr.Zero)
            {
                HaltExecution();
                return;
            }
            POINT pt = new POINT();
            ClientToScreen(hWnd, ref pt);
            RectL.Text = (selection.left - pt.x).ToString();
            RectT.Text = (selection.top - pt.y).ToString();
            RectR.Text = (selection.right - pt.x).ToString();
            RectB.Text = (selection.bottom - pt.y).ToString();
            RectH.Text = (selection.bottom - selection.top + 1).ToString();
            RectW.Text = (selection.right - selection.left + 1).ToString();
        }

        private void RestoreSize(object sender, RoutedEventArgs e)
        {
            hWnd = FindWindow(ClassName, WindowName);
            if (hWnd == IntPtr.Zero)
            {
                HaltExecution();
                return;
            }
            RECT windowRect = new RECT();
            if (GetWindowRect(hWnd, ref windowRect))
            {
                lastPosX = windowRect.left;
                lastPosY = windowRect.top;
            }
            SetWindowPos(hWnd, IntPtr.Zero, 100, 100, 1300, 625, 0);
        }

        private void DetectWindow(object sender, RoutedEventArgs e)
        {
            hWnd = FindWindow(ClassName, WindowName);
            if (hWnd == IntPtr.Zero)
            {
                HaltExecution();
                return;
            }
            BeginExecution();
        }
        bool StateCheck()
        {
            hWnd = FindWindow(ClassName, WindowName);
            if (hWnd == IntPtr.Zero)
            {
                HaltExecution();
                return false;
            }
            RECT clientRect = new RECT();
            GetClientRect(hWnd, ref clientRect);
            int clientWidth = clientRect.right;
            int clientHeight = clientRect.bottom;
            if (clientHeight != standardClientHeight || clientWidth != standardClientWidth)
            {
                ErrorMessage.Content = "Window size changed";
                ErrorMessage.Visibility = Visibility.Visible;
                return false;
            }
            ErrorMessage.Visibility = Visibility.Hidden;
            return true;
        }

        private void BeginView(object sender, RoutedEventArgs e)
        {
            StateIndicator.Content = "Running";
            StateIndicator.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 125, 230, 100));
            viewTimer.Start();
        }

        private void StopView(object sender, RoutedEventArgs e)
        {
            StateIndicator.Content = "Stopped";
            StateIndicator.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 255, 155, 155));
            viewTimer.Stop();
        }

        private void LoadPoint(object sender, RoutedEventArgs e)
        {
            ClickHelper clickHelper = new ClickHelper();
            clickHelper.Closed += UpdatePoint;
            clickHelper.Show();
        }

        private void UpdatePoint(object sender, EventArgs e)
        {
            hWnd = FindWindow(ClassName, WindowName);
            if (hWnd == IntPtr.Zero)
            {
                HaltExecution();
                return;
            }
            POINT pt = new POINT();
            ClientToScreen(hWnd, ref pt);
            PointX.Text = (clickPos.x - pt.x).ToString();
            PointY.Text = (clickPos.y - pt.y).ToString();
            if ((clickPos.x - pt.x) >= 0 && (clickPos.x - pt.x) < standardClientWidth && (clickPos.y - pt.y) >= 0 && (clickPos.y - pt.y) < standardClientHeight)
            {
                System.Windows.Media.Color c = GetPixelColor((clickPos.x - pt.x), (clickPos.y - pt.y));
                ColorRDisplay.Text = c.R.ToString();
                ColorGDisplay.Text = c.G.ToString();
                ColorBDisplay.Text = c.B.ToString();
                ColorDisplay.Background = new SolidColorBrush(c);
            }
        }

        private void SendBackWindow(object sender, RoutedEventArgs e)
        {
            hWnd = FindWindow(ClassName, WindowName);
            if (hWnd == IntPtr.Zero)
            {
                HaltExecution();
                return;
            }
            SetWindowPos(hWnd, IntPtr.Zero, lastPosX, lastPosY, 1300, 625, 0);
        }

        private void CalculateHash(object sender, RoutedEventArgs e)
        {
            if (StateCheck())
            {
                Bitmap scrshot = new Bitmap(standardClientWidth, standardClientHeight);
                POINT pt = new POINT();
                ClientToScreen(hWnd, ref pt);
                using (Graphics g = Graphics.FromImage(scrshot))
                {
                    g.CopyFromScreen(pt.x, pt.y, 0, 0, new System.Drawing.Size(standardClientWidth, standardClientHeight));
                }
                using (MemoryStream ms = new MemoryStream())
                {
                    BitmapImage bmp = new BitmapImage();

                    scrshot.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                    ms.Position = 0;
                    bmp.BeginInit();
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    if (selection.right >= selection.left && selection.bottom >= selection.top
                        && (selection.left - pt.x) >= 0 && (selection.right - pt.x) >= 0
                        && (selection.top - pt.y) >= 0 && (selection.bottom - pt.y) >= 0
                        && (selection.left - pt.x) < standardClientWidth && (selection.right - pt.x) < standardClientWidth
                        && (selection.top - pt.y) < standardClientHeight && (selection.bottom - pt.y) < standardClientHeight)
                    {
                        BitmapSource cbmp = CropImage(bmp, selection.left - pt.x, selection.top - pt.y, selection.right - pt.x, selection.bottom - pt.y);
                        ulong hash = GetHash(cbmp);
                        HashDisplay.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 255, 255, 255));
                        HashDisplay.Text = hash.ToString();
                    }
                    else
                    {
                        HashDisplay.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 205, 53, 53));
                        HashDisplay.Text = "";
                    }
                }
            }
        }
    }
}
