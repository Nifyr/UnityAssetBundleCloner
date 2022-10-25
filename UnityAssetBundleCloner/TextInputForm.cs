using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UnityAssetBundleCloner
{
    public partial class TextInputForm : Form
    {
        private string outString;
        public string OutString { get { return outString; } }

        public TextInputForm(string caption, string message, string defaultInput)
        {
            InitializeComponent();

            this.Text = caption;
            label.Text = message;
            outString = defaultInput;
            richTextBox.Text = defaultInput;
        }

        private void OutTextChanged(object sender, EventArgs e)
        {
            outString = richTextBox.Text;
        }

        private void ConfirmClick(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
