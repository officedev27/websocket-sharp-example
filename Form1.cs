using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Websockets
{


    public partial class Form1 : Form
    {
        private WSServer ws;
        private int cnt = 0;

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            ws = null;
        }

        public void AddData(object sender, string msg)
        {
            listBox1.Items.Add(msg);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            ws = new();
            timer1.Interval = 10;
            timer1.Tick += Timer1_Tick;
            timer1.Enabled = true;
        }

        private void Timer1_Tick(object sender, EventArgs e)
        {
            timer1.Stop();
            //messages are pumped rather quickly through the WSServer->WSClient queues to demonstrate that
            //this setup efficiently dispatches messages through the queues. The only bottleneck should be 
            //the bandwidth of the socket connection.
            for (int i = 0; i < 100; i++)
            {
                ws.SendMessage(cnt.ToString());
                cnt++;
            }
            timer1.Start();
        }
    }
}
