﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using Chat.Helper;

namespace Server.Server
{
    public class ChatServer
    {
        #region Fields
        private TcpListener tcpServer;
        private Thread mainThread;
        private readonly int portNumber;
        private bool isRunning;
        private NetworkInterface networkInterface;
        private string serverName;
        private readonly Dictionary<string, Socket> userNames = new Dictionary<string, Socket>(); 
        public event EventHandler ClientConnected;
        public event EventHandler ClientDisconnected;
        #endregion

        #region Constructor

        public ChatServer(int portNumber, object networkInterface, string serverName)
        {
            this.serverName = serverName;
            this.portNumber = portNumber;
            this.networkInterface = networkInterface as NetworkInterface;
        }

        #endregion

        #region Server Start/Stop

        public void StartServer()
        {
            mainThread = new Thread(StartListen);
            mainThread.Start();
        }
        /// <summary>
        /// Server listens to specified port and accepts connection from client
        /// </summary>
        public void StartListen()
        {
            tcpServer = new TcpListener(IPAddress.Any,portNumber);
            tcpServer.Start();
            
            isRunning = true;
            // Keep accepting client connection
            while (isRunning)
            {
                if (!tcpServer.Pending())
                {
                    Thread.Sleep(500);
                    continue;
                }
                // New client is connected, call event to handle it
                var clientThread = new Thread(NewClient);
                var tcpClient = tcpServer.AcceptTcpClient();
                tcpClient.ReceiveTimeout = 20000;
                clientThread.Start(tcpClient.Client);
            }
        }
        /// <summary>
        /// Method to stop TCP communication, it kills the thread and closes clients' connection
        /// </summary>
        public void StopServer()
        {
            isRunning = false;
            if (tcpServer == null)
                return;
            userNames.Clear();
            tcpServer.Stop();
        }

        #endregion

        #region Add/Remove Clients
        public void NewClient(object obj)
        {
            ClientAdded(this, new CustomEventArgs((Socket)obj));
        }


        public void ClientAdded(object sender, EventArgs e)
        {
            var socket = ((CustomEventArgs) e).ClientSocket;
            var bytes = new byte[1024];
            var bytesRead = socket.Receive(bytes);
           
            var newUserName = Encoding.Unicode.GetString(bytes, 0, bytesRead);

            if (userNames.ContainsKey(newUserName))
            {
                SendNameAlreadyExist(socket, newUserName);
                return;
            }

            userNames.Add(newUserName, socket);
            OnClientConnected(socket, newUserName);

            foreach (var user in userNames)
                SendUsersList(user.Value, user.Key, newUserName, ChatHelper.Connected);
           
            var state = new ChatHelper.StateObject
            {
                WorkSocket = socket
            };
            
            socket.BeginReceive(state.Buffer, 0, ChatHelper.StateObject.BufferSize, 0,
            OnReceive, state);
        }


        public void SendNameAlreadyExist(Socket clientSocket, string name)
        {
            var response = string.Format("{0}|{1}", name, ChatHelper.NameExist);
            var bytes = Encoding.Unicode.GetBytes(response);
            clientSocket.Send(bytes);
        }

        public void SendUsersList(Socket clientSocket, string userName, string changedUser, string serverState)
        {
            var userList = string.Format("{0}|{1}|{2}|{3}|{4}|{5}", ChatHelper.Message, ChatHelper.Server,
                string.Join(",", userNames.Keys.Where(u => u != userName).ToArray()), changedUser, serverState,
                serverName);
            var bytes = Encoding.Unicode.GetBytes(userList);
            clientSocket.Send(bytes);
        }

        public void OnReceive(IAsyncResult ar)
        {
            var state = ar.AsyncState as ChatHelper.StateObject;
            if (state == null)
                return;
            var handler = state.WorkSocket;
            if (!handler.Connected)
                return;
            try
            {
                var bytesRead = handler.EndReceive(ar);
                if (bytesRead <= 0)
                    return;

                ParseRequest(state,bytesRead,handler);
                
                // Restore receiving
                handler.BeginReceive(state.Buffer, 0, ChatHelper.StateObject.BufferSize, 0,
                OnReceive, state);

            }
            catch (Exception)
            {
                DisconnectClient(handler);
            }
        }

        private void ParseRequest(ChatHelper.StateObject state,int bytesRead, Socket incomingClient)
        {
            var recievedString = Encoding.Unicode.GetString(state.Buffer, 0, bytesRead);
            var str = recievedString.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            
            var messageType = str[0];
            
            switch (messageType)
            {
                case ChatHelper.Heartbeat:
                    if (!incomingClient.Connected)
                        DisconnectClient(incomingClient);
                break;

                default:
                    Socket clientSocket;
                    if (userNames.TryGetValue(str[1], out clientSocket))
                    {
                        var bytes = Encoding.Unicode.GetBytes(recievedString);
                        clientSocket.Send(bytes);
                    }
                break;
            }
        }


        public void DisconnectClient(Socket clientSocket)
        {
            var userName = userNames.FirstOrDefault(k => k.Value == clientSocket).Key;
            OnClientDisconnected(clientSocket, userName);

            clientSocket.Close();
            userNames.Remove(userName);

            foreach (var user in userNames)
                SendUsersList(user.Value, user.Key, userName, ChatHelper.Disconnected);
        }

       #endregion

        #region Event Invokers

        protected virtual void OnClientConnected(Socket clientSocket, string name)
        {
            var handler = ClientConnected;
            if (handler != null) handler(name, new CustomEventArgs(clientSocket));
        }

        protected virtual void OnClientDisconnected(Socket clientSocket, string name)
        {
            var handler = ClientDisconnected;
            if (handler != null) handler(name, new CustomEventArgs(clientSocket));
        }

        #endregion
    }
}