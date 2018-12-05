using System;
using System.Collections;
using System.IO;
using System.Linq;
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
        private Hashtable clientSockets = new Hashtable();
        private const int BUFFER_SIZE = 1024 * 32000;
        private static readonly byte[] buffer = new byte[BUFFER_SIZE];
        public delegate void Changed(object T);
        public event Changed ChangeEvent;
        public object ExMessage;
        public object ClientRequest;
        public object ClientConnect;
        public object ClientExit;
        public bool IsSKCreate { get; private set; } = false;
        public string IpServer { get; private set; } = "127.0.0.1";

        public bool SetupServer()
        {
            if (!serverSocket.Connected)
            {
                serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    serverSocket.Bind(new IPEndPoint(IPAddress.Any, 777));
                    serverSocket.Listen(0);
                    serverSocket.BeginAccept(AcceptCallback, null);
                    IpServer = GetIpServer();
                    IsSKCreate = true;
                    return true;
                }
                catch
                {
                    ExMessage = "Không thể tạo Server!";
                    ChangeEvent?.Invoke(ExMessage);
                    return false;
                }
            }
            else
            {
                return true;
            }
        }
        private string GetIpServer()
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
        /// <summary>
        /// Close all connected client (we do not need to shutdown the server socket as its connections
        /// are already closed with the clients).
        /// </summary>
        public void CloseAllSockets()
        {
            foreach (DictionaryEntry socket in clientSockets)
            {
                Socket sk = socket.Value as Socket;
                sk.Shutdown(SocketShutdown.Both);
                sk.Close();
            }
            IsSKCreate = false;
            clientSockets.Clear();
            serverSocket.Close();
        }
        public bool RespondClientRq(object Object, string IpClient)
        {
            Socket clientsk = clientSockets[IpClient] as Socket;
            try
            {
                byte[] data = SerializeData(Object);
                clientsk.Send(data);
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
                ExMessage = "Mất kết nối!";
                ChangeEvent?.Invoke(ExMessage);
                return;
            }
            clientSockets.Remove("127.0.0.1");
            clientSockets.Add("127.0.0.1", socket);
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
                    received = 1024 * 32000;
                }
            }
            catch (SocketException)
            {
                current.Close();
                return;
            }

            byte[] recBuf = new byte[received];
            Array.Copy(buffer, recBuf, received);
            string text = DeserializeData(recBuf).ToString();
            string[] arrText = text.Split(new char[] { '&' }, 2);
            switch (arrText.First())
            {
                case "ipclient":
                    {
                        Socket skUS = current;
                        clientSockets.Remove(arrText[1]);
                        clientSockets.Add(arrText[1], skUS);
                        clientSockets.Remove("127.0.0.1");
                        ClientConnect = arrText[1];
                        ChangeEvent?.Invoke(ClientConnect);
                        break;
                    }
                case "exit":
                    {
                        Socket skUS = current;
                        try
                        {
                            skUS.Shutdown(SocketShutdown.Both);
                            skUS.Close();
                        }
                        catch { }
                        clientSockets.Remove(arrText[1]);
                        ClientExit = arrText[1];
                        ChangeEvent?.Invoke(ClientExit);
                        return;
                    }
                case "rcvrq":
                    {
                        Hashtable rqclient = new Hashtable();
                        foreach (DictionaryEntry ipclient in clientSockets)
                        {
                            if (ipclient.Value == current)
                            {
                                rqclient.Remove(ipclient.Key);
                                rqclient.Add(ipclient.Key, arrText[1]);
                            }
                        }
                        byte[] data = SerializeData(RespondRequest(rqclient, arrText[1]));
                        current.Send(data);
                        break;
                    }
                default:
                    {
                        break;
                    }
            }
            try
            {
                current.BeginReceive(buffer, 0, BUFFER_SIZE, SocketFlags.None, ReceiveCallback, current);
            }
            catch
            {

            }
        }
        private string RespondRequest(Hashtable Request, string Text)
        {
            string rsp = "Không xác định được yêu cầu!";
            try
            {
                ClientRequest = Request;
                ChangeEvent?.Invoke(ClientRequest);
                rsp = "Đã nhận được yêu cầu: " + Text;
            }
            catch { }
            return rsp;
        }
        /// <summary>
        /// Nén đối tượng thành mảng byte[]
        /// </summary>
        /// <param name="o"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Giải nén mảng byte[] thành đối tượng object
        /// </summary>
        /// <param name="theByteArray"></param>
        /// <returns></returns>
        private object DeserializeData(byte[] theByteArray)
        {
            if (theByteArray.Length == 0)
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
        /// <summary>
        /// Lấy ra IP V4 của card mạng đang dùng
        /// </summary>
        /// <param name="_type"></param>
        /// <returns></returns>
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
}
