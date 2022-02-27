using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WebSocketSharp; //Tested with version 1.0.3-rc11 from NuGet which was the latest at the time this was made.
using WebSocketSharp.Server;

namespace Websockets
{
    static class Program
    {
        public static Form1 form1;

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            form1 = new(); //maintain reference to form to assign DataReceivedEvent delegates. Mainly needed for WSClient. 
            Application.Run(form1);
        }
    }

    /// <summary>
    /// Wrapper class to manage WebSocketServer. If this were a production system it would not be tied to Form1 like it is in this example.
    /// This class is the place to add custom client management such as data feed subscriptions and such, but that exercise is left to you.
    /// </summary>
    public class WSServer
    {
        private CancellationTokenSource cancellationTokenSource = new();
        private ConcurrentQueue<string> queueSend = new();
        private WebSocketServer wss;
        private WebSocketServiceHost rootHost;

        public delegate void DataReceivedEventHandler(object sender, string item);
        public event DataReceivedEventHandler DataReceivedEvent;

        public WSServer()
        {
            this.DataReceivedEvent += Program.form1.AddData;

            wss = new WebSocketServer("ws://localhost:5000");
            wss.AddWebSocketService<WSClient>("/");
            wss.Start();
            foreach (WebSocketServiceHost host in wss.WebSocketServices.Hosts)
            {
                if (host.Path == "/")
                    rootHost = host;
            }
            Task.Run(() => MessageDispatchLoop(cancellationTokenSource), cancellationTokenSource.Token);

            //Load messages
            DataReceivedEvent?.Raise(this, "Service is running: " + wss.IsListening);
            foreach (WebSocketServiceHost host in wss.WebSocketServices.Hosts)
            {
                DataReceivedEvent?.Raise(this, "Host on path: " + host.Path);
            }
        }

        ~WSServer()
        {
            wss.Stop();
            wss = null;
            Dispose();
        }

        public void SendMessage(string msg)
        {
            queueSend.Enqueue(msg);
        }

        private async Task MessageDispatchLoop(CancellationTokenSource connectionCts)
        {
            while (true)
            {
                connectionCts.Token.ThrowIfCancellationRequested();

                string text;
                if (queueSend.TryDequeue(out text))
                {
                    text = text + " backlog WSServer " + queueSend.Count.ToString();
                    if (wss.IsListening)
                    {
                        //Dispatches messages to WSClient classes so message disbursement does not slow down with multiple clients.
                        //The messages could be sent directly from here using session.Context.WebSocket.Send() but you would bottleneck
                        //with multiple clients.
                        foreach (var session in rootHost.Sessions.Sessions)
                        {
                            //session comes in as IWebSocketSession but can be cast to access custom methods such as SendMessage here.
                            WSClient client = (WSClient)session;
                            client.SendMessage(text);
                        }
                    }
                    else
                    {
                        //the server is shut down. add handling here.
                        //WSClient uses connectionCts.Cancel() to let the middleware class close out, but an unexpected server shutdown needs more design consideration.
                    }
                } else
                {
                    //If no messages are queued this will prevent the loop from eating cpu. Without this await, this loop uses about 8% cpu on a Ryzen 9. 0% with this await.
                    //Used in the else clause so if massive amounts of messages are being sent, they are dispatched without delay.
                    await Task.Delay(1);
                }
            }
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls
        public void Dispose()
        {
            if (!disposedValue)
            {
                try
                {
                    //Logger.Debug("BaseWSClient{0} Disposing.", InstanceID.ToString());
                }
                catch { }

                //not sure.. just to be sure that no error prevents everything from disposing, i use the poor man's on error resume next
                try
                {
                    cancellationTokenSource.Cancel();
                }
                catch { }

                try
                {
                    cancellationTokenSource.Dispose();
                }
                catch { }


                disposedValue = true;
            }
        }
        #endregion
    }

    /// <summary>
    /// A middleware class to manage a connection.
    /// </summary>
    public class WSClient : WebSocketBehavior
    {
        private ConcurrentQueue<string> queueSend = new();
        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        public delegate void DataReceivedEventHandler(object sender, string item);
        public event DataReceivedEventHandler DataReceivedEvent;

        public WSClient()
        {
            this.DataReceivedEvent += Program.form1.AddData;
            Task.Run(() => SocketWriteLoop(cancellationTokenSource), cancellationTokenSource.Token);
        }

        ~WSClient()
        {
            Dispose();
        }

        protected override void OnOpen()
        {
            string msg = base.ID + " has connected.";
            DataReceivedEvent?.Raise(this, msg);
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            DataReceivedEvent?.Raise(this, e.Data);
        }

        protected override void OnClose(CloseEventArgs e)
        {
            string msg = base.ID + " has disconnected.";
            DataReceivedEvent?.Raise(this, msg);
        }

        public void SendMessage(string msg)
        {
            queueSend.Enqueue(msg);
        }

        private async Task SocketWriteLoop(CancellationTokenSource connectionCts)
        {
            while (true)
            {
                connectionCts.Token.ThrowIfCancellationRequested();
                if (base.State == WebSocketState.Closed)
                {
                    //This is important. If the socket closes and this loop continues, the class will never shut down until the application is closed. Would cause memory leaks.
                    connectionCts.Cancel();
                    break;
                }

                string text;
                if (queueSend.TryDequeue(out text))
                {
                    text = text + " backlog WSClient " + queueSend.Count.ToString();
                    if (base.State == WebSocketState.Open)
                    {
                        //no need to use send async here since this is a dedicated thread for sending. Socket reads should 
                        //happen simultaneous to writes since the websocket library uses its own threads.
                        base.Send(text);
                    } 
                        
                }
                else
                {
                    await Task.Delay(1);
                }
            }
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls
        public void Dispose()
        {
            if (!disposedValue)
            {
                try
                {
                    //Logger.Debug("BaseWSClient{0} Disposing.", InstanceID.ToString());
                }
                catch { }

                //not sure.. just to be sure that no error prevents everything from disposing, i use the poor man's on error resume next
                try
                {
                    cancellationTokenSource.Cancel();
                }
                catch { }

                try
                {
                    cancellationTokenSource.Dispose();
                }
                catch { }


                disposedValue = true;
            }
        }
        #endregion
    }

    public static class EventExtensions
    {
        /// <summary>Raises the event (on the UI thread if available).</summary>
        /// <param name="multicastDelegate">The event to raise.</param>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">An EventArgs that contains the event data.</param>
        /// <returns>The return value of the event invocation or null if none.</returns>
        public static object Raise<T>(this MulticastDelegate multicastDelegate, object sender, T e)
        {
            object retVal = null;

            MulticastDelegate threadSafeMulticastDelegate = multicastDelegate;
            if (threadSafeMulticastDelegate != null)
            {
                foreach (Delegate d in threadSafeMulticastDelegate.GetInvocationList())
                {
                    var synchronizeInvoke = d.Target as ISynchronizeInvoke;
                    if ((synchronizeInvoke != null) && synchronizeInvoke.InvokeRequired)
                    {
                        try
                        {
                            retVal = synchronizeInvoke.EndInvoke(synchronizeInvoke.BeginInvoke(d, new[] { sender, e }));
                        }
                        catch { }
                    }
                    else
                    {
                        retVal = d.DynamicInvoke(new[] { sender, e });
                    }
                }
            }

            return retVal;
        }
    }
}
