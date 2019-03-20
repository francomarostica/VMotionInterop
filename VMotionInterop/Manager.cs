using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace VMotionInterop
{
    public class Manager
    {
        private int port = 0;
        private int Connections = 0;
        private TcpListener tcpListener;
        private Socket serverSocket;
        private List<Socket> clients = new List<Socket>();
        private byte[] buffer;

        public delegate void OnConnectHandler();
        public delegate void OnVMotionConnectedHandler();
        public delegate void OnDataReceivedHandler(string data);
        /// <summary>
        /// Event ocurrs when the VMotion request information about an specified product
        /// </summary>
        /// <param name="ArticleId">The article of requested stock info</param>
        /// <returns>Return the quantity of current stock</returns>
        public delegate double OnRequestStockInfoHandler(string ArticleId);
        public delegate double OnRequestProductHandler(string ArticleId);


        public event OnConnectHandler OnConnect;
        public event OnVMotionConnectedHandler OnVMotionConnected;
        public event OnDataReceivedHandler OnDataReceived;
        public event OnRequestStockInfoHandler OnRequestStockInfo;
        public event OnRequestProductHandler OnRequestProduct;

        public Manager(int port = 6040)
        {
            this.port = port;
        }

        public void Connect()
        {
            serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            serverSocket.Bind(new IPEndPoint(IPAddress.Any, port));
            serverSocket.Listen(0);
            serverSocket.BeginAccept(new AsyncCallback(AcceptCallBack), null);

            var evento = OnConnect;
            evento?.Invoke();
        }

        private void AcceptCallBack(IAsyncResult ar)
        {
            Socket clientSocket = serverSocket.EndAccept(ar);
            buffer = new byte[clientSocket.ReceiveBufferSize];
            clients.Add(clientSocket);
            clientSocket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(ReceiveCallback), clientSocket);

            var evento = OnVMotionConnected;
            evento?.Invoke();

            serverSocket.BeginAccept(new AsyncCallback(AcceptCallBack), null);
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            string dataLog = "";
            Socket socket = (Socket)ar.AsyncState;
            socket.EndReceive(ar);

            int bytesRead = 0;
            NetworkStream stream = new NetworkStream(socket);
            StringBuilder stringBuilder = new StringBuilder();

            byte[] dataBuff = new byte[1024];
            do
            {
                bytesRead = stream.Read(dataBuff, 0, dataBuff.Length);
                stringBuilder.AppendFormat("{0}", Encoding.ASCII.GetString(dataBuff, 0, bytesRead));
            }
            while (bytesRead > 0 && stream.DataAvailable);

            string text = stringBuilder.ToString();

            if (!text.StartsWith("<WWKS")) return;

            buffer = new byte[socket.ReceiveBufferSize];
            socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(ReceiveCallback), socket);

            string WWKSVersion = "";
            string DateStr = DateTime.Now.ToString("yyyy-MM-dd");
            string TimeStr = DateTime.Now.ToString("HH:mm");

            string xmlString = CleanInvalidXmlChars(text);

            XmlDocument xmlDocument = new XmlDocument();
            xmlDocument.LoadXml(xmlString);

            if (xmlDocument.DocumentElement.Name == "WWKS")
            {
                if (xmlDocument.DocumentElement.HasAttributes)
                {
                    foreach (XmlAttribute xmlAttribute in xmlDocument.DocumentElement.Attributes)
                    {
                        if (xmlAttribute.Name.ToLower() == "version")
                        {
                            WWKSVersion = xmlAttribute.Value;
                        }
                    }
                }
            }

            foreach (XmlNode xmlNode in xmlDocument.DocumentElement)
            {
                if (xmlNode.Name == "StockInfoRequest")
                {
                    string Id = "";
                    string Source = "";
                    string Destination = "";
                    double Stock = 0;

                    XmlNode criteriaNode = xmlNode.SelectSingleNode("Criteria");
                    string ArticleId = criteriaNode.Attributes[0].Value;

                    //Parsing request
                    foreach (XmlAttribute xmlAttribute in xmlNode.Attributes)
                    {
                        if (xmlAttribute.Name == "Id") Id = xmlAttribute.Value;
                        if (xmlAttribute.Name == "Source") Source = xmlAttribute.Value;
                        if (xmlAttribute.Name == "Destination") Destination = xmlAttribute.Value;
                    }

                    dataLog = "Request stock info for article id: " + ArticleId;

                    var eventoStock = OnRequestStockInfo;
                    if (eventoStock != null) Stock = eventoStock.Invoke(ArticleId);

                    string responseString = "";
                    responseString += "<WWKS Version=\"2.0\" TimeStamp=\"" + DateStr + "T" + TimeStr + "Z\">";
                    responseString += "<StockInfoResponse Id=\"" + Id + "\" Source=\"" + Destination + "\" Destination=\"" + Source + "\">";
                    responseString += "<Article Id=\"" + ArticleId + "\" Quantity=\"" + Stock + "\" />";
                    responseString += "</StockInfoResponse>";
                    responseString += "</WWKS>";
                    byte[] data = Encoding.ASCII.GetBytes(responseString);
                    socket.BeginSend(data, 0, data.Length, SocketFlags.None, new AsyncCallback(SendCallback), socket);
                }
                if(xmlNode.Name == "OutputRequest")
                {
                    string Id = "";
                    string Source = "";
                    string Destination = "";
                    double Quantity = 0;

                    XmlNode criteriaNode = xmlNode.SelectSingleNode("Criteria");
                    string ArticleId = criteriaNode.Attributes[0].Value;

                    //Parsing request
                    foreach (XmlAttribute xmlAttribute in xmlNode.Attributes)
                    {
                        if (xmlAttribute.Name == "Id") Id = xmlAttribute.Value;
                        if (xmlAttribute.Name == "Source") Source = xmlAttribute.Value;
                        if (xmlAttribute.Name == "Destination") Destination = xmlAttribute.Value;
                    }

                    dataLog = "Request output for article id: " + ArticleId;
                    var eventoCart = OnRequestProduct;
                    if (eventoCart != null) Quantity = eventoCart.Invoke(ArticleId);

                    string responseString = "";
                    responseString += "<WWKS Version=\"2.0\" TimeStamp=\"" + DateStr + "T" + TimeStr + "Z\">";
                    responseString += "<OutputResponse Id=\"" + Id + "\" Source=\"" + Destination + "\" Destination=\"" + Source + "\" Stock=\"" + Quantity + "\">";
                    responseString += "<Criteria ArticleId=\"" + ArticleId + "\" Stock=\"" + Quantity + "\" />";
                    responseString += "</OutputResponse>";
                    responseString += "</WWKS>";
                    byte[] data = Encoding.ASCII.GetBytes(responseString);
                    socket.BeginSend(data, 0, data.Length, SocketFlags.None, new AsyncCallback(SendCallback), socket);

                    /*
                    <WWKS Version="2.0" TimeStamp="2019-02-21T12:24:31Z">
                      <OutputRequest Id="173" Source="333" Destination="999">
                        <Details Priority="Normal" OutputDestination="1" />
                        <Criteria ArticleId="7793640215547" Quantity="1" />
                      </OutputRequest>
                    </WWKS>
                    */
                }
                if (xmlNode.Name == "StatusRequest")
                {
                    string Id = "";
                    string Source = "";
                    string Destination = "";
                    string State = "Ready";

                    //Parsing request
                    foreach (XmlAttribute xmlAttribute in xmlNode.Attributes)
                    {
                        if (xmlAttribute.Name == "Id") Id = xmlAttribute.Value;
                        if (xmlAttribute.Name == "Source") Source = xmlAttribute.Value;
                        if (xmlAttribute.Name == "Destination") Destination = xmlAttribute.Value;
                    }

                    string responseString = "";
                    responseString += "<WWKS Version=\"2.0\" TimeStamp=\"" + DateStr + "T" + TimeStr + "Z\">";
                    responseString += "<StatusResponse Id=\"" + Id + "\" Source=\"" + Destination + "\" Destination=\"" + Source + "\" State=\"" + State + "\">";
                    responseString += "<Component Type=\"BoxSystem\" Description=\"Box system\" State=\"" + State + "\"/>";
                    responseString += "</StatusResponse>";
                    responseString += "</WWKS>";

                    dataLog = "Request status (KeepAlive)";

                    byte[] data = Encoding.ASCII.GetBytes(responseString);
                    socket.BeginSend(data, 0, data.Length, SocketFlags.None, new AsyncCallback(SendCallback), socket);
                }
            }

            var evento = OnDataReceived;
            evento?.Invoke(dataLog);
        }

        private void SendCallback(IAsyncResult ar)
        {
            Socket socket = (Socket)ar.AsyncState;
            socket.EndSend(ar);
        }

        private static string CleanInvalidXmlChars(string text)
        {
            string re = @"[^\x09\x0A\x0D\x20-\xD7FF\xE000-\xFFFD\x10000-x10FFFF]";
            string cleanedXML = Regex.Replace(text, re, "");
            cleanedXML = cleanedXML.Replace("\n", "");
            cleanedXML = cleanedXML.Replace("\r", "");
            return cleanedXML;
        }
    }
}
