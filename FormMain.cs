﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace KCHWoodpecker
{
    public partial class FormMain : Form
    {
        string dir = null;
        FileSystemWatcher watcher;
        WebSocketServer wssrv;
        public FormMain()
        {
            InitializeComponent();
            lblVer.Text = $"Версия {Application.ProductVersion}";
            if (Process.GetProcessesByName("KCHWoodpecker").Length > 1)
            {
                MessageBox.Show("Дятел уже запущен", $"Дятел {Application.ProductVersion}", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Process.GetCurrentProcess().Kill();
            }
            Process p = FindESTProcess();
            while (p == null)
            {
                new FormWaitingProcess().ShowDialog();
                p = FindESTProcess();
            }
            dir = Path.Combine(Path.GetDirectoryName(p.MainModule.FileName), "Logs");
            StartSpectating();
            Console.WriteLine(dir);

            wssrv = new WebSocketServer(8089);
            wssrv.AddWebSocketService<WSBehaviour>("/");
            wssrv.Start();
        }

        void MassSend(string msg)
        {
            wssrv.WebSocketServices["/"].Sessions.Broadcast(msg);
        }

        Process FindESTProcess()
        {
            Process p = null;
            foreach (Process process in Process.GetProcesses())
            {
                if (process.ProcessName.StartsWith("ES_Taxi"))
                {
                    p = process;
                    Console.WriteLine(p.ProcessName);
                    break;
                }
            }
            return p;
        }

        void StartSpectating()
        {
            if (watcher != null) watcher.EnableRaisingEvents = false;
            watcher = new FileSystemWatcher();
            watcher.Path = dir;
            watcher.Filter = "*.console";
            watcher.EnableRaisingEvents = true;

            Dictionary<string, long> sizes = new Dictionary<string, long>();
            watcher.Changed += (object sender, FileSystemEventArgs e) =>
            {
                FileStream s = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                long readTo = s.Length;
                long readFrom = sizes.ContainsKey(e.FullPath) ? sizes[e.FullPath] : readTo;
                if (!sizes.ContainsKey(e.FullPath)) sizes[e.FullPath] = 0;
                sizes[e.FullPath] = readTo;
                s.Seek(readFrom, SeekOrigin.Begin);
                int length = (int)(readTo - readFrom);
                if (length == 0) return;
                byte[] buffer = new byte[length];
                s.Read(buffer, 0, length);
                string result = Encoding.Default.GetString(buffer);
                ProcessData(result);
            };
            Console.WriteLine("Watching " + dir);
        }

        void StopSpectating()
        {
            watcher.EnableRaisingEvents = false;
        }

        void ProcessData(string data)
        {
            Log(data, "raw", false);
            string[] lines = data.Replace("\r", "").Split('\n');

            foreach (string rawLine in lines)
            {
                string line = rawLine.Contains(" >>> ") ? rawLine.Substring(rawLine.IndexOf(" >>> ") + 5) : rawLine;
                line = line.Trim();
                if (line.Length == 0) continue;
                Log(line, "line");
                if (line.Contains("@192")) Log($"Something like phone: \"{line}\"");
                if (line.Contains("pc-take-order")) Log($"Something like order: \"{line}\"");
                if (line.StartsWith("CounterPathPhone:client_OnCallStatus: sip:")) ProcessPhone(line.Replace("CounterPathPhone:client_OnCallStatus: sip:", ""));
                else if (line.StartsWith("{\"pc-take-order\":{")) ProcessOrder();
            }
        }



        void ProcessOrder()
        {
            MassSend("order");
        }

        void ProcessPhone(string data)
        {
            string numpart = data.Split(' ')[0];
            string prefix = numpart.Contains("*") ? numpart.Split('*')[0] : "0";
            string number = (numpart.Contains("*") ? numpart.Split('*')[1] : numpart).Replace("@192.168.93.254", "");

            string status = data.Split(' ')[1];
            Log($"STATUS {prefix}*{number}: {status} ||| {data}");

            switch (status)
            {
                case "ECS_Incoming":
                case "i":
                case "4":
                    Log($"Incoming {prefix}*{number}");
                    MassSend($"action:{prefix}*{number} incoming");
                    break;
                case "ECS_Connected":
                case "ECS_Dialing":
                case "c":
                case "1":
                case "2":
                case "5":
                    Log($"Connected {prefix}*{number}");
                    MassSend($"action:{prefix}*{number} connected");
                    break;
                case "ECS_Disconnected":
                case "ECS_Ringback":
                case "d":
                case "6":
                    Log($"Disconnected {prefix}*{number}");
                    MassSend($"action:{prefix}*{number} disconnected");
                    break;
                default:
                    Log($"Unknown {prefix}*{number} {status}");
                    break;
            }
        }

        void Log(string msg, string file = "default", bool formatLine = true)
        {
            //formatLine ? $"\r\n[{DateTime.Now}] {msg}" : msg
            if (formatLine) msg = $"\r\n[{DateTime.Now}] {msg}";
            Console.Write(msg);

#if DEBUG
            if (dir != null) File.AppendAllText(Path.Combine(dir, $"{DateTime.Now.ToShortDateString()}.{file}.wp.log"), msg);

            string yesterday = DateTime.Now.AddDays(-1).ToShortDateString();
            foreach (string fn in Directory.GetFiles(dir))
            {
                string n = Path.GetFileName(fn);
                if (n.EndsWith(".wp.log") && !n.StartsWith(DateTime.Now.ToShortDateString()) && !n.StartsWith(DateTime.Now.AddDays(-1).ToShortDateString()))
                {
                    File.Delete(fn);
                }
            }
#endif
        }

        private void btnFlushLogs_Click(object sender, EventArgs e)
        {
            DateTime started = DateTime.Now;
#if DEBUG
            FileStream lockTest = null;
            if (MessageBox.Show($"Создать заблокированный файл?", "Отладка", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                lockTest = new FileStream(Path.Combine(dir, "lockTest.console"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
#endif
            btnFlushLogs.Text = "Удаляем логи ЕСТ...";
            btnFlushLogs.Enabled = false;
            StopSpectating();
            foreach (string fn in Directory.GetFiles(dir))
            {
                if (fn.EndsWith(".console"))
                {
                    DateTime now = DateTime.Now;
                    while (true) try
                        {
                            File.Delete(fn);
                            break;
                        }
                        catch {
                            if ((DateTime.Now - now).TotalSeconds > 5)
                            {
                                MessageBox.Show($"Не удалось удалить файл \"{Path.GetFileName(fn)}\", программа держит его занятым", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                break;
                            }
                        }
                }
            }
            while ((DateTime.Now - started).TotalMilliseconds < 500) { }
            StartSpectating();
            btnFlushLogs.Text = "Появились задержки";
            btnFlushLogs.Enabled = true;
#if DEBUG
            if (lockTest != null)
            {
                lockTest.Close();
                File.Delete(Path.Combine(dir, "lockTest.console"));
            }
#endif
        }
    }

    public class WSBehaviour : WebSocketBehavior {
        protected override void OnOpen()
        {
            Thread t = new Thread(() =>
            {
                Send("action:0*0 connected");
                Thread.Sleep(250);
                Send("action:0*0 disconnected");
                Thread.Sleep(250);
                Send("action:0*0 connected");
                Thread.Sleep(250);
                Send("action:0*0 disconnected");
            });
            t.Start();
        }
    }
}
