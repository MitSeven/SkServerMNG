using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;

namespace SkServerMNG
{
    [Serializable]
    public class SkServerManager
    {
        private static Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private const int BUFFER_SIZE = 1024 * 32000;
        private static readonly byte[] buffer = new byte[BUFFER_SIZE];
        private int rdconnect=100;
        public delegate void Changed(object T, ModeEventArgs e);
        public event Changed ChangeEvent;
        public string IpServer
        {
            get
            {
                string IP_current;
                try
                {
                    IP_current = GetLocalIPv4(NetworkInterfaceType.Ethernet);
                    if (string.IsNullOrEmpty(IP_current))
                    {
                        IP_current = GetLocalIPv4(NetworkInterfaceType.Wireless80211);
                    }
                }
                catch
                {
                    IP_current = "127.0.0.1";
                }
                return IP_current;
            }
            private set { }
        }
        public bool IsSKCreate { get; private set; } = false;
        public Hashtable ClientSockets { get; private set; } = new Hashtable();
        private object exMessage;
        public bool SetupServer
        {
            get
            {
                if (!serverSocket.Connected)
                {
                    serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        serverSocket.Bind(new IPEndPoint(IPAddress.Any, 777));
                        serverSocket.Listen(0);
                        serverSocket.BeginAccept(AcceptCallback, null);
                        IsSKCreate = true;
                        return true;
                    }
                    catch
                    {
                        exMessage = "Cannot create Server!";
                        ChangeEvent?.Invoke(exMessage, new ModeEventArgs(ModeEvent.SocketMessage));
                        IsSKCreate = false;
                        return false;
                    }
                }
                else
                {
                    return true;
                }
            }
        }
        public bool CloseAllSockets
        {
            get
            {
                IsSKCreate = false;
                rdconnect = 100;
                try
                {
                    foreach (DictionaryEntry socket in ClientSockets)
                    {
                        try
                        {
                            Socket sk = socket.Value as Socket;
                            SendtoClient(new KeyValuePair<TypeSend, object>(TypeSend.Exit, null), sk);
                            sk.Shutdown(SocketShutdown.Both);
                            sk.Close();
                        }
                        catch { }
                    }
                }
                catch { }
                try
                {
                    ClientSockets.Clear();
                    serverSocket.Close();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
        public bool RespondClient(object Object, string IpClient)
        {
            try
            {
                Socket clientsk = ClientSockets[IpClient] as Socket;
                return SendtoClient(new KeyValuePair<TypeSend, object>(TypeSend.Respond, Object), clientsk);
            }
            catch
            {
                return false;
            }
        }
        private bool SendtoClient(KeyValuePair<TypeSend, object> keyValuePair, Socket socket)
        {
            try
            {
                socket.Send(SerializeData(keyValuePair));
                return true;
            }
            catch
            {
                return false;
            }
        }
        private void AcceptCallback(IAsyncResult AR)
        {
            Socket socket;
            try
            {
                socket = serverSocket.EndAccept(AR);
            }
            catch (ObjectDisposedException)
            {
                exMessage = "Lost connect!";
                ChangeEvent?.Invoke(exMessage, new ModeEventArgs((int)ModeEvent.SocketMessage));
                return;
            }
            ClientSockets.Remove("user"+rdconnect.ToString());
            ClientSockets.Add("user" + rdconnect.ToString(), socket);
            rdconnect++;
            socket.BeginReceive(buffer, 0, BUFFER_SIZE, SocketFlags.None, ReceiveCallback, socket);
            serverSocket.BeginAccept(AcceptCallback, null);
        }
        private void ReceiveCallback(IAsyncResult AR)
        {
            Socket current = (Socket)AR.AsyncState;
            int received;
            try
            {
                if (current.Connected)
                {
                    received = current.EndReceive(AR);
                }
                else
                {
                    received = 0;
                    Array.Clear(buffer, 0, BUFFER_SIZE);
                }
            }
            catch (SocketException)
            {
                current.Close();
                return;
            }
            byte[] recBuf = new byte[received];
            Array.Copy(buffer, recBuf, received);
            object text = null;
            try
            {
                text = DeserializeData(recBuf).ToString();
            }
            catch
            {
                text = System.Text.Encoding.ASCII.GetString(recBuf);
            }
            CommandEx(text, current);//xử lý lệnh nhận được từ client
            try
            {
                current.BeginReceive(buffer, 0, BUFFER_SIZE, SocketFlags.None, ReceiveCallback, current);
            }
            catch { }
        }
        private void CommandEx(object text, Socket current)
        {
            try
            {
                KeyValuePair<TypeReceived, object> command = (KeyValuePair<TypeReceived, object>)text;
                switch (command.Key)
                {
                    case TypeReceived.IpAdress:
                        {
                            int lastconn = rdconnect - 1;
                            Socket skUS = current;
                            ClientSockets.Remove(command.Value);
                            ClientSockets.Add(command.Value, skUS);
                            ClientSockets.Remove("user" + lastconn.ToString());
                            exMessage = command.Value;
                            ChangeEvent?.Invoke(exMessage, new ModeEventArgs(ModeEvent.ClientConnect));
                            break;
                        }
                    case TypeReceived.Exit:
                        {
                            Socket skUS = current;
                            try
                            {
                                skUS.Shutdown(SocketShutdown.Both);
                                skUS.Close();
                            }
                            catch { }
                            ClientSockets.Remove(command.Value);
                            exMessage = command.Value;
                            ChangeEvent?.Invoke(exMessage, new ModeEventArgs(ModeEvent.ClientExit));
                            return;
                        }
                    case TypeReceived.Object:
                        {
                            KeyValuePair<string, object> rqclient=new KeyValuePair<string, object>();
                            foreach (DictionaryEntry ipclient in ClientSockets)
                            {
                                if (ipclient.Value == current)
                                {
                                    rqclient = new KeyValuePair<string, object>(ipclient.Key.ToString(),command.Value);
                                }
                            }
                            exMessage = rqclient;
                            ChangeEvent?.Invoke(exMessage, new ModeEventArgs(ModeEvent.ClientRequest));
                            SendtoClient(new KeyValuePair<TypeSend, object>(TypeSend.Received, "Received: " + command.Value.ToString()), current);
                            break;
                        }
                    default:
                        {
                            break;
                        }
                }
            }
            catch
            {
                try
                {
                    byte[] data = System.Text.Encoding.ASCII.GetBytes("Received: " + text.ToString());
                    current.Send(data);
                }
                catch { }
            }
        }
        private byte[] SerializeData(Object o)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    BinaryFormatter bf1 = new BinaryFormatter();
                    bf1.Serialize(ms, o);
                    return ms.ToArray();
                }
            }
            catch
            {
                return new byte[0];
            }
        }
        private object DeserializeData(byte[] theByteArray)
        {
            if ((theByteArray.Length == 0))
            {
                return null;
            }
            else
            {
                try
                {
                    using (MemoryStream ms = new MemoryStream(theByteArray))
                    {
                        BinaryFormatter bf1 = new BinaryFormatter();
                        ms.Position = 0;
                        return bf1.Deserialize(ms);
                    }
                }
                catch
                {
                    return null;
                }
            }
        }
        private string GetLocalIPv4(NetworkInterfaceType _type)
        {
            string output = "";
            foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (item.NetworkInterfaceType == _type && item.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (UnicastIPAddressInformation ip in item.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            output = ip.Address.ToString();
                        }
                    }
                }
            }
            return output;
        }
    }
    public class ModeEventArgs
    {
        private ModeEvent _mode;
        public ModeEvent ModeEvent { get => _mode; set => _mode = value; }
        public ModeEventArgs(ModeEvent mode) => ModeEvent = mode;
    }
    public enum ModeEvent
    {
        SocketMessage,
        ClientConnect,
        ClientExit,
        ClientRequest,
    }
    public enum TypeReceived
    {
        IpAdress,
        Exit,
        Object,
    }
    public enum TypeSend
    {
        Exit,
        Respond,
        Received,
    }
}
