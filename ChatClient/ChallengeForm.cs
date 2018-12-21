using System;
using System.Windows.Forms;

namespace ChatClient
{
    public partial class ChallengeForm : Form
    {
        public bool accept;
        public Form1 parentForm;

        public string challengeMessage;

        public ChallengeForm(Form1 parent)
        {
            parentForm = parent;
            InitializeComponent();
        }

        private void ChallengeForm_Load(object sender, EventArgs e)
        {
            textBox1.Text = challengeMessage;
        }

        public void Stop()
        {
            parentForm._challengeAccept = accept;
            this.Close();
        }

        private void yesButton_MouseClick(object sender, MouseEventArgs e)
        {
            accept = true;
            Stop();
        }

        private void noButton_MouseClick(object sender, MouseEventArgs e)
        {
            accept = false;
            Stop();
        }
    }
}
