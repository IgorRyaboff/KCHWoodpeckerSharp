﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KCHWoodpecker
{
    public partial class FormWaitingProcess : Form
    {
        public FormWaitingProcess()
        {
            InitializeComponent();
            Text = $"Дятел {Application.ProductVersion}";
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void btnQuit_Click(object sender, EventArgs e)
        {
            Process.GetCurrentProcess().Kill();
        }
    }
}
