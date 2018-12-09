using System;
using System.Windows.Forms;

namespace ChatClient
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Form1 clientForm = new Form1();
            Application.Run(clientForm);
        }
    }
}
