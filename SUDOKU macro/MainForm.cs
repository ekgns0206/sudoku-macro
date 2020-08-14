using IronPython.Hosting;
using IronPython.Runtime;
using Microsoft.Scripting.Hosting;
using Microsoft.WindowsAPICodePack.Dialogs;
using Module;
using OpenCvSharp;
using SUDOKU_macro.Module;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace SUDOKU_macro
{
    public partial class MainForm : Form
    {
        public const int MAXIMUM_IDLE_TIME = 1000 * 30;

        private Random random = new Random();
        private Sprite _sprite;
        private Status _status;
        private ScriptRuntime pythonRuntime;
        private string _lastStatusName = string.Empty;
        private Stopwatch _stopwatch = new Stopwatch();

        public PythonDictionary Sprite { get; private set; }
        public PythonDictionary Status { get; private set; }
        public PythonDictionary Timers { get; private set; }
        public PythonDictionary State { get; private set; }

        public App app { get; private set; }
        public Detector Detector { get; private set; }

        public bool InitStopWatch { get; set; }

        public string History
        {
            set
            {
                this.historyTextBox.Invoke(new MethodInvoker(delegate ()
                {
                    this.historyTextBox.AppendText(string.Format("[{0}] {1}", DateTime.Now.ToString("HH:mm/ss"), value));
                    this.historyTextBox.AppendText(Environment.NewLine);
                }));
            }
        }

        private string StatusName
        {
            set
            {
                this.statusNameLabel.Invoke(new MethodInvoker(delegate ()
                {
                    this.statusNameLabel.Text = value;
                }));
            }
        }

        private bool running = false;
        public bool Running
        {
            get
            {
                return this.running;
            }
            set
            {
                this.running = value;
            }
        }

        public MainForm()
        {
            this.Timers = new PythonDictionary();
            this.State = new PythonDictionary();

            InitializeComponent();

            this.spriteResourceTextBox.Text = Path.Combine(Directory.GetCurrentDirectory(), "templates", "sprite.ksp");
            this.statusResourceTextBox.Text = Path.Combine(Directory.GetCurrentDirectory(), "templates", "status.kst");
        }

        private void App_OnFrame(OpenCvSharp.Mat frame)
        {
            try
            {
                this.streamingBox.Frame = frame;

                if (this.Running)
                    this.ExecPython(this.frameScript.Text, frame);

                var points = new Dictionary<string, Point>();
                var statusName = this.Detector.Detect(frame, out points);
                if (statusName == null)
                {
                    this.StatusName = "UNKNOWN";
                    return;
                }
                this.StatusName = statusName;

                if (this._lastStatusName.Equals(statusName))
                {
                    this._stopwatch.Stop();
                    if (this._stopwatch.ElapsedMilliseconds > MAXIMUM_IDLE_TIME || this.InitStopWatch)
                    {
                        this.History = "초기화합니다.";
                        this._lastStatusName = string.Empty;
                        this._stopwatch.Reset();
                        this.InitStopWatch = false;
                    }

                    this._stopwatch.Start();
                }
                else
                {
                    this.ExecPython(this._status[statusName].Script, frame, points.ToDict(), true);
                    this._lastStatusName = statusName;
                    this._stopwatch.Reset();
                }
            }
            catch (Exception e)
            {
                return;
            }
        }

        private object ExecPython(string fname, Mat frame = null, object parameter = null, bool log = false)
        {
            try
            {
                var script = string.Format("scripts/{0}", fname);
                dynamic scope = this.pythonRuntime.UseFile(script);
                var ret = scope.callback(this, frame, this.Sprite, this.State, parameter);
                if (log)
                {
                    this.History = string.Format("{0} 스크립트를 호출했습니다.", script);
                    if (ret != null)
                        this.History = string.Format("{0}의 반환값 : {1}", script, ret);
                }

                return ret;
            }
            catch (System.IO.FileNotFoundException)
            {
                return null;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return null;
            }
        }

        private void streamingBox_MouseDown(object sender, MouseEventArgs e)
        {
            if(e.Button == MouseButtons.Left)
            {
                if(this.app != null)
                    this.app.KeyPress((Keys)'W');
            }
        }

        private void randomTimer_Tick(object status)
        {
            var script = status as string;
            this.ExecPython(script);
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if(this.app != null)
                this.app.Release();

            if(this.pythonRuntime != null)
                this.pythonRuntime.Shutdown();

            this.ExecPython(this.disposeScript.Text);
        }

        private void noxPlayerFrameBox_MouseDown(object sender, MouseEventArgs e)
        {

        }

        public System.Threading.Timer SetTimer(string name, int interval, string script)
        {
            if(this.Timers.ContainsKey(name))
                return null;

            var createdTimer = new System.Threading.Timer(this.randomTimer_Tick, script, interval, System.Threading.Timeout.Infinite);
            this.Timers.Add(name, createdTimer);

            return createdTimer;
        }

        public void UnsetTimer(string name)
        {
            if(this.Timers.ContainsKey(name) == false)
                return;

            var timer = this.Timers[name] as System.Threading.Timer;
            timer.Dispose();
            this.Timers.Remove(name);
        }

        private void execute_Click(object sender, EventArgs e)
        {
            this.Running = !this.Running;
            this.execute.Text = this.Running ? "중지" : "시작";
            this.settingPanel.Enabled = !this.Running;

            if (this.Running)
            {
                try
                {
                    if (this.app != null)
                        this.app.Dispose();

                    this._lastStatusName = string.Empty;
                    this.loadPythonModules(this.pythonPath.Text);
                    this.loadResources(this.spriteResourceTextBox.Text, this.statusResourceTextBox.Text);

                    this.app = new App(this.appClassName.Text, this.softwareType.Checked ? OperationType.Software : OperationType.Hardware);
                    this.app.OnFrame += App_OnFrame;
                    this.app.Start();

                    this.ExecPython(this.initializeScript.Text);
                }
                catch (Exception exc)
                {
                    MessageBox.Show(exc.Message);
                }
            }
            else
            {
                this.app.Release();
                this.app = null;
            }
        }

        private void loadPythonModules(string path)
        {
            if(path.Length == 0)
                throw new Exception("You must set a valid python path.");

            if(Directory.Exists(path) == false)
                throw new Exception(string.Format("{0} path does not exist.", path));

            if(File.Exists(Path.Combine(path, "python.exe")) == false)
                throw new Exception(string.Format("Cannot find python.exe file in {0}.", path));

            if(this.pythonRuntime != null)
                this.pythonRuntime.Shutdown();

            this.pythonRuntime = Python.CreateRuntime();
            var engine = this.pythonRuntime.GetEngine("IronPython");
            var paths = engine.GetSearchPaths();
            paths.Add(path);
            paths.Add(Path.Combine(path, "DLLs"));
            paths.Add(Path.Combine(path, "lib"));
            paths.Add(Path.Combine(path, "lib\\site-packages"));
            paths.Add(Path.Combine(Directory.GetCurrentDirectory(), "scripts"));
            engine.SetSearchPaths(paths);
        }

        private void loadResources(string spriteFileName, string statusFileName)
        {
            this._sprite = new Sprite();
            if (this._sprite.load(spriteFileName) == false)
                throw new Exception("스프라이트 파일을 읽어올 수 없습니다.");

            this._status = new Status(this._sprite);
            if (this._status.load(statusFileName) == false)
                throw new Exception("상태 파일을 읽어올 수 없습니다.");

            this.Sprite = this._sprite.ToDict();
            this.Status = this._status.ToDict();
            this.Detector = new Detector(this._sprite, this._status);
        }

        private void browseSpriteButton_Click(object sender, EventArgs e)
        {
            var dialog = new CommonOpenFileDialog();
            dialog.InitialDirectory = Path.GetDirectoryName(this.spriteResourceTextBox.Text);

            if(dialog.ShowDialog() != CommonFileDialogResult.Ok)
                return;

            this.spriteResourceTextBox.Text = dialog.FileName;
        }

        private void browsePython_Click(object sender, EventArgs e)
        {
            var dialog = new CommonOpenFileDialog();
            dialog.InitialDirectory = Path.GetDirectoryName(this.statusResourceTextBox.Text);

            if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
                return;

            this.pythonPath.Text = dialog.FileName;
        }

        private Nullable<int> MatchNumber(Mat source)
        {
            var maximum = 0.0;
            var percentage = 0.0;
            var selected = new Nullable<int>();
            for (var num = 9; num > 0; num--)
            {
                var location = this._sprite[num.ToString()].MatchTo(source, ref percentage, null as Nullable<Point>, null as Nullable<Size>, true, false);
                if (location == null)
                    continue;

                if (percentage > maximum)
                {
                    selected = num;
                    maximum = percentage;
                }
            }

            return selected;
        }

        public IronPython.Runtime.List Partition(Mat frame)
        {
            var components = new Mat[3, 3, 3, 3];
            var componentsInt = new Nullable<int>[3, 3, 3, 3];
            var begin = new Point(11, 159);
            var size = new Size(180, 180);

            for (var row = 0; row < 3; row++)
            {
                for (var col = 0; col < 3; col++)
                {
                    var partition = frame.Clone(new Rect(new Point(begin.X + (size.Width + 1) * col, begin.Y + (size.Height + 1) * row), size));
                    this.ExtractPartitionComponents(partition, row, col, components);
                }
            }

            var pythonList = new List();
            for (var i1 = 0; i1 < 3; i1++)
            {
                var outerRowList = new List();
                for (var i2 = 0; i2 < 3; i2++)
                {
                    var outerColumnList = new List();
                    for (var i3 = 0; i3 < 3; i3++)
                    {
                        var innerRowList = new List();
                        for (var i4 = 0; i4 < 3; i4++)
                        {
                            var innerColumnList = new List();
                            try
                            {
                                var num = this.MatchNumber(components[i1, i2, i3, i4]);
                                if (num == null)
                                    throw new Exception();

                                //Console.WriteLine("{0} {1} {2} {3} : {4}", i1, i2, i3, i4, num);
                                innerRowList.Add(num);
                            }
                            catch (Exception)
                            {
                                for(var i = 0; i < 9; i++)
                                    innerColumnList.Add(i + 1);
                                innerRowList.Add(innerColumnList);
                                //Console.WriteLine("{0} {1} {2} {3} : {4}", i1, i2, i3, i4, "unknown");
                            }
                        }
                        outerColumnList.Add(innerRowList);
                    }
                    outerRowList.Add(outerColumnList);
                }
                pythonList.Add(outerRowList);
            }
            Console.WriteLine(Environment.NewLine + Environment.NewLine + Environment.NewLine);

            return pythonList;
        }

        private void ExtractPartitionComponents(Mat frame, int sourceRow, int sourceColumn, Mat[,,,] frames)
        {
            var size = new Size(60, 60);
            for (var row = 0; row < 3; row++)
            {
                for (var col = 0; col < 3; col++)
                {
                    var component = frame.Clone(new Rect(new Point(size.Width * col, size.Height * row), size));
                    frames[sourceRow, sourceColumn, row, col] = component;
                }
            }
        }

        public void Sleep(int m)
        {
            Thread.Sleep(m);
        }
    }
}