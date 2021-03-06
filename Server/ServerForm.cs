using System;
using System.Drawing;
using System.Collections.Generic;
using System.Collections;
using System.Text;
using System.Linq;
using System.Windows.Forms;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Xml;

namespace Server
{
    //The commands for interaction between the server and the client
    enum Command
    {
        Login,          //Log into the server
        Logout,         //Logout of the server
        Message,        //Send a text message to all the chat clients
        List,           //Get a list of users in the chat room from the server
        DataInfo,       //Get a list of music filenames, evidence, and currently unused characters that the server has loaded
        PacketSize,     //Get the size in bytes of the next incoming packet so we can size our receiving packet accordingly. Used for receiving the DataInfo packets.
        ChangeMusic,    //Tells all clients to start playing the selected audio file
        ChangeHealth,
        Evidence,
        SendEvidence,
        Present,
        Disconnect,
        Null            //No command
    }

    public partial class ServerForm : Form
    {
        //The ClientInfo structure holds the required information about every
        //client connected to the server
        struct ClientInfo
        {
            public Socket socket;   //Socket of the client
            public string strName;  //Name by which the user logged into the chat room
            //public string character; //Character (file)name that the user is playing as
        }

        //The collection of all clients logged into the room (an array of type ClientInfo)
        ArrayList clientList;

        List<Evidence> eviList = new List<Evidence>();

        //The main socket on which the server listens to the clients
        Socket serverSocket;

        //The main socket on which the server communicates with the masterserver
        Socket masterSocket;

        private string masterserverIP;

        private bool isClosing = false;

        byte[] byteData = new byte[1048576];
        byte[] allData;

        public ServerForm()
        {
            clientList = new ArrayList();
            InitializeComponent();
        }

        private void ServerForm_Load(object sender, EventArgs e)
        {
            updateCheck(true);
            masterserverIP = iniParser.GetMasterIP();
            if (masterserverIP == null)
            {
                MessageBox.Show("Failed to find masterserver IP in " + '"' + "masterserver.ini" + '"' + ". \r\n Assuming masterserver is locally hosted.", "AODXServer", MessageBoxButtons.OK);
                masterserverIP = "127.0.0.1";
            }

            if (iniParser.GetServerInfo().Split('|')[8] == "1")
            {
                eviList = iniParser.GetEvidenceData();
                foreach (Evidence item in eviList)
                {
                    if (item.name == null)
                        item.name = "Item name";
                    if (item.desc == null)
                        item.desc = "Item description.";
                }
            }

            userNumStat.Text = "Users Online: " + clientList.Count;

            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    localIPLabel.Text = "Server IP Address: " + ip.ToString();
                    break;
                }
                //throw new Exception("Local IP Address Not Found!");
            }


            try
            {
                //We are using TCP sockets
                masterSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                //The masterserver is listening on port 1002
                IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Parse(masterserverIP), 1002);

                //Connect to the masterserver
                masterSocket.BeginConnect(ipEndPoint, new AsyncCallback(OnConnect), null);
            }
            catch (Exception ex)
            {
                if (Program.debug)
                    MessageBox.Show(ex.Message + "\r\n" + ex.StackTrace.ToString(), "AODXServer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnConnect(IAsyncResult ar)
        {
            try
            {
                masterSocket.EndConnect(ar);
                sendServerInfo(masterSocket);

                //Start listening for info refresh requests from the masterserver
                masterSocket.BeginReceive(byteData, 0, byteData.Length, SocketFlags.None, new AsyncCallback(OnReceive), masterSocket);

                //Start accepting client connections
                //We are using TCP sockets
                serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                //Assign the any IP of the machine and listen on port number 1000
                IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Any, 1000);

                //Bind and listen on the given address
                serverSocket.Bind(ipEndPoint);
                serverSocket.Listen(4);

                //Accept the incoming clients
                serverSocket.BeginAccept(new AsyncCallback(OnAccept), null);
            }
            catch (SocketException)
            { }
            catch (ObjectDisposedException)
            { }
            catch (Exception ex)
            {
                if (Program.debug)
                    MessageBox.Show(ex.Message + "\r\n" + ex.StackTrace.ToString(), "AODXServer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnAccept(IAsyncResult ar)
        {
            try
            {
                if (!isClosing)
                {
                    Socket clientSocket = serverSocket.EndAccept(ar);

                    //Start listening for more clients
                    serverSocket.BeginAccept(new AsyncCallback(OnAccept), null);

                    //Send the client our basic info (description, connected users)
                    sendServerInfo(clientSocket);

                    //Now that the client is connected, start receiving the commands from her
                    clientSocket.BeginReceive(byteData, 0, byteData.Length, SocketFlags.None, new AsyncCallback(OnReceive), clientSocket);
                }
            }
            catch (SocketException)
            { }
            catch (ObjectDisposedException)
            { }
            catch (Exception ex)
            {
                if (Program.debug)
                    MessageBox.Show(ex.Message + "\r\n" + ex.StackTrace.ToString(), "AODXServer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnReceive(IAsyncResult ar)
        {
            try
            {
                Socket receiveSocket = (Socket)ar.AsyncState;
                receiveSocket.EndReceive(ar);

                if (!isClosing & receiveSocket.Connected)
                {
                    //If the masterserver is requesting our info (description, user count, etc.)
                    if (byteData[0] == 101 | receiveSocket.RemoteEndPoint == masterSocket.RemoteEndPoint)
                    {
                        sendServerInfo(masterSocket);
                        masterSocket.BeginReceive(byteData, 0, byteData.Length, SocketFlags.None, new AsyncCallback(OnReceive), masterSocket);
                    }
                    else if (byteData[0] == 103)
                        receiveSocket.Close();
                    else if (byteData[0] == 8)
                        sendEvidence(receiveSocket);
                    else if (byteData[0] == 9)
                    {
                        EviData msg = new EviData(byteData);

                        Evidence evi = new Evidence();
                        evi.name = msg.strName;
                        evi.desc = msg.strDesc;
                        evi.note = msg.strNote;
                        evi.index = msg.index;

                        using (MemoryStream ms = new MemoryStream(msg.dataBytes))
                        {
                            evi.icon = Image.FromStream(ms, false, true);
                        }

                        bool found = false;
                        foreach (Evidence item in eviList)
                        {
                            if (item.index == evi.index)
                            {
                                found = true;
                                item.name = evi.name;
                                item.note = evi.note;
                                item.desc = evi.desc;
                                item.icon = evi.icon;
                                break;
                            }
                        }
                        if (found == false)
                        {
                            evi.index = eviList.Count;
                            eviList.Add(evi);
                        }

                        EviData dat = msg;
                        dat.cmdCommand = Command.Evidence;
                        byte[] msgToSend = dat.ToByte();

                        foreach (ClientInfo client in clientList)
                        {
                            client.socket.BeginSend(msgToSend, 0, msgToSend.Length, SocketFlags.None, new AsyncCallback(OnSend), client.socket);
                        }
                        byteData = new byte[1048576];

                        //System.Threading.Thread.Sleep(3000);
                        receiveSocket.BeginReceive(byteData, 0, byteData.Length, SocketFlags.None, new AsyncCallback(OnReceive), receiveSocket);
                    }
                    //else if (byteData[0] == 0 | byteData[0] == 1 | byteData[0] == 2 | byteData[0] == 3 | byteData[0] == 4 | byteData[0] == 5 | byteData[0] == 6 | byteData[0] == 7 | byteData[0] == 10 | byteData[0] == 11 | byteData[0] == 12)
                    else if (receiveSocket.RemoteEndPoint != masterSocket.RemoteEndPoint)
                        parseMessage(receiveSocket);
                    else
                    {
                        //TO DO: Look into this, something is definitely wrong when we receive 2 or 3 garbage messages from clients every time they present evidence!!!
                        appendTxtLogSafe("<<" + receiveSocket.RemoteEndPoint.ToString() + " send an invalid packet with the first byte: " + byteData[0] + ">>\r\n");
                        receiveSocket.BeginReceive(byteData, 0, byteData.Length, SocketFlags.None, new AsyncCallback(OnReceive), receiveSocket);
                    }
                }
            }
            catch (SocketException)
            { }
            catch (ObjectDisposedException)
            { }
            catch (Exception ex)
            {
                if (Program.debug & !isClosing)
                    MessageBox.Show(ex.Message + ".\r\n" + ((Socket)ar.AsyncState)?.RemoteEndPoint?.ToString() + "\r\n" + ex.StackTrace.ToString(), "AODXServer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void sendServerInfo(Socket socketToUse)
        {
            try
            {
                List<byte> msgToSend = new List<byte>();
                msgToSend.Add(101);
                string info = iniParser.GetServerInfo() + "|" + userNumStat.Text.Split(new string[] { ": " }, StringSplitOptions.None)[1];
                msgToSend.AddRange(BitConverter.GetBytes(info.Length));
                msgToSend.AddRange(Encoding.UTF8.GetBytes(info));
                byte[] message = msgToSend.ToArray();

                socketToUse.BeginSend(message, 0, message.Length, SocketFlags.None, new AsyncCallback(OnSend), socketToUse);
            }
            catch (SocketException)
            { }
            catch (ObjectDisposedException)
            { }
            catch (Exception ex)
            {
                if (Program.debug)
                    MessageBox.Show(ex.Message + "\r\n" + ex.StackTrace.ToString(), "AODXServer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void sendEvidence(Socket sendHere)
        {
            foreach (Evidence evi in eviList)
            {
                EviData dat = new EviData(evi.name, evi.desc, evi.note, evi.icon, evi.index);
                byte[] msg = dat.ToByte();
                sendHere.BeginSend(msg, 0, msg.Length, SocketFlags.None, new AsyncCallback(OnSend), sendHere);
                System.Threading.Thread.Sleep(100);
            }
        }

        private void parseMessage(Socket clientSocket)
        {
            try
            {
                if (isClosing != true)
                {
                    //Transform the array of bytes received from the user into an
                    //intelligent form of object Data
                    Data msgReceived = new Data(byteData);

                    //We will send this object in response the users request
                    Data msgToSend = new Data();

                    byte[] message;

                    bool isBanned = false;
                    if (File.Exists("base/banlist.ini"))
                    {
                        using (StreamReader r = new StreamReader("base/banlist.ini"))
                        {
                            while (!r.EndOfStream)
                            {
                                if (r.ReadLine().StartsWith(clientSocket.RemoteEndPoint.ToString().Split(':')[0]))
                                {
                                    isBanned = true;
                                    break;
                                }
                            }
                        }
                    }

                    //If the message is to login, logout, or simple text message
                    //then when sent to others, the type of the message remains the same
                    msgToSend.cmdCommand = msgReceived.cmdCommand;
                    msgToSend.strName = msgReceived.strName;

                    switch (msgReceived.cmdCommand)
                    {
                        case Command.Login:
                            //When a user logs in to the server then we add her to our
                            //list of clients

                            if (msgReceived.strName == null || msgReceived.strName == "")
                            {
                                appendTxtLogSafe("<<" + clientSocket.RemoteEndPoint.ToString() + " send an invalid Login packet>>\r\n");
                                clientSocket.BeginReceive(byteData, 0, byteData.Length, SocketFlags.None, new AsyncCallback(OnReceive), clientSocket);
                                return;
                            }

                            ClientInfo clientInfo = new ClientInfo();
                            clientInfo.socket = clientSocket;
                            clientInfo.strName = msgReceived.strName;
                            clientList.Add(clientInfo);
                            appendLstUsersSafe(msgReceived.strName + " - " + ((IPEndPoint)clientSocket.RemoteEndPoint).Address.ToString());

                            //Set the text of the message that we will broadcast to all users
                            msgToSend.strMessage = "<<<" + msgReceived.strName + " has entered the courtroom>>>";
                            userNumStat.Text = "Users Online: " + clientList.Count;


                            /* //DO THE SAME STUFF AS IF THE CLIENT SENT A LIST COMMAND
                            //Send the names of all users in the chat room to the new user
                            msgToSend.cmdCommand = Command.List;
                            msgToSend.strName = null;
                            msgToSend.strMessage = null;

                            //Collect the names of the user in the chat room
                            foreach (ClientInfo client in clientList)
                            {
                                //To keep things simple we use asterisk as the marker to separate the user names
                                msgToSend.strMessage += client.strName + "*";
                            } */

                            message = msgToSend.ToByte();

                            //Send the name of the users in the chat room
                            //clientSocket.BeginSend(message, 0, message.Length, SocketFlags.None, new AsyncCallback(OnSend), clientSocket);
                            break;

                        case Command.Logout:
                            //When a user wants to log out of the server then we search for her 
                            //in the list of clients and close the corresponding connection

                            int nIndex = 0;
                            foreach (ClientInfo client in clientList)
                            {
                                if (client.socket == clientSocket)
                                {
                                    clientList.RemoveAt(nIndex);
                                    //removeLstUsersSafe(client.strName + " - " + client.character);
                                    removeLstUsersSafe(client.strName + " - " + ((IPEndPoint)client.socket.RemoteEndPoint).Address.ToString());
                                    break;
                                }
                                ++nIndex;
                            }

                            clientSocket.Close();

                            msgToSend.strMessage = "<<<" + msgReceived.strName + " has left the courtroom>>>";
                            userNumStat.Text = "Users Online: " + clientList.Count;
                            break;

                        case Command.ChangeMusic:
                            if (msgReceived.strMessage != null & msgReceived.strName != null)
                            {
                                msgToSend.cmdCommand = Command.ChangeMusic;
                                msgToSend = msgReceived;
                                appendTxtLogSafe("<<<" + msgReceived.strName + " changed the music to " + msgReceived.strMessage + ">>>\r\n");
                            }
                            break;

                        case Command.ChangeHealth:
                            if (msgReceived.strMessage != null & msgReceived.strName != null)
                            {
                                msgToSend = msgReceived;
                            }
                            break;

                        case Command.Message:
                        case Command.Present:
                            //Set the text of the message that we will broadcast to all users
                            msgToSend = msgReceived;
                            msgToSend.strMessage = msgReceived.strName + ": " + msgReceived.strMessage;
                            break;

                        case Command.List:
                            //Send the names of all users in the chat room to the new user
                            msgToSend.cmdCommand = Command.List;
                            msgToSend.strName = null;
                            msgToSend.strMessage = null;

                            //Collect the names of the user in the chat room
                            foreach (ClientInfo client in clientList)
                            {
                                //To keep things simple we use asterisk as the marker to separate the user names
                                msgToSend.strMessage += client.strName + "*";
                            }

                            message = msgToSend.ToByte();

                            //Send the name of the users in the chat room
                            clientSocket.BeginSend(message, 0, message.Length, SocketFlags.None, new AsyncCallback(OnSend), clientSocket);
                            break;

                        case Command.PacketSize:
                            //First, send all of the current case's evidence data
                            sendEvidence(clientSocket);

                            msgToSend.cmdCommand = Command.DataInfo;
                            msgToSend.strName = null;
                            msgToSend.strMessage = "";

                            List<string> allChars = iniParser.GetCharList();
                            List<string> charsInUse = new List<string>();
                            foreach (string cName in allChars)
                            {
                                if (clientList != null && clientList.Count > 0)
                                {
                                    foreach (ClientInfo client in clientList)
                                    {
                                        if (client.strName == cName)
                                        {
                                            charsInUse.Add(cName);
                                        }
                                    }
                                }
                            }

                            foreach (string cName in charsInUse)
                            {
                                allChars.Remove(cName);
                            }

                            msgToSend.strMessage += allChars.Count + "|";
                            foreach (string cName in allChars)
                            {
                                msgToSend.strMessage += cName + "|";
                            }

                            List<string> songs = iniParser.GetMusicList();
                            msgToSend.strMessage += songs.Count + "|";
                            foreach (string song in songs)
                            {
                                msgToSend.strMessage += song + "|";
                            }

                            message = msgToSend.ToByte(true);
                            //byte[] evidence = iniParser.GetEvidenceData().ToArray();
                            //if (evidence.Length > 0)
                            //    allData = message.Concat(evidence).ToArray();
                            //else
                            allData = message;

                            Data sizeMsg = new Data();
                            if (!isBanned)
                            {
                                sizeMsg.cmdCommand = Command.PacketSize;
                                sizeMsg.strMessage = allData.Length.ToString();
                            }
                            else
                            {
                                sizeMsg.cmdCommand = Command.Disconnect;
                            }

                            byte[] sizePacket = sizeMsg.ToByte();
                            clientSocket.BeginSend(sizePacket, 0, sizePacket.Length, SocketFlags.None, new AsyncCallback(OnSend), clientSocket);
                            break;

                        case Command.DataInfo:
                            if (!isBanned)
                                clientSocket.BeginSend(allData, 0, allData.Length, SocketFlags.None, new AsyncCallback(OnSend), clientSocket);
                            break;
                    }

                    if (msgToSend.cmdCommand != Command.List & msgToSend.cmdCommand != Command.DataInfo & msgToSend.cmdCommand != Command.PacketSize)   //List messages are not broadcasted
                    {
                        message = msgToSend.ToByte(); // TO DO: REMOVE THE OTHER CALLS TO THIS IN THE INDIVIDUAL SWITCH CASES, THEY ARE PROBABLY REDUNDANT

                        foreach (ClientInfo clientInfo in clientList)
                        {
                            //if the command is login, dont send the login notification to the person who's logging in, its redundant
                            if (clientInfo.socket != clientSocket || msgToSend.cmdCommand != Command.Login)
                            {
                                //Send the message to all users
                                clientInfo.socket.BeginSend(message, 0, message.Length, SocketFlags.None, new AsyncCallback(OnSend), clientInfo.socket);
                            }
                        }
                        if (msgToSend.cmdCommand != Command.ChangeMusic & msgToSend.cmdCommand != Command.ChangeHealth)
                            if (msgReceived.callout <= 3)
                                appendTxtLogSafe(msgToSend.strMessage.Split('|')[0] + "\r\n");
                    }

                    //If the user is logging out then we need not listen from her
                    if (msgReceived.cmdCommand != Command.Logout)
                    {
                        //Start listening to the messages sent by the user
                        clientSocket.BeginReceive(byteData, 0, byteData.Length, SocketFlags.None, new AsyncCallback(OnReceive), clientSocket);
                    }
                }
            }
            catch (ObjectDisposedException)
            { }
            catch (SocketException)
            { }
            catch (Exception ex)
            {
                if (Program.debug)
                    MessageBox.Show(ex.Message + "\r\n" + ex.StackTrace.ToString(), "AODXServer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ServerForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                List<byte> msgToSend = new List<byte>();
                msgToSend.Add(103);
                byte[] message = msgToSend.ToArray();
                if (masterSocket.Connected)
                    masterSocket.BeginSend(message, 0, message.Length, SocketFlags.None, new AsyncCallback(OnSendClose), null);
                masterSocket.Close();
            }
            catch (SocketException)
            { }
            catch (ObjectDisposedException)
            { }
            catch (Exception ex)
            {
                if (Program.debug)
                    MessageBox.Show(ex.Message + "\r\n" + ex.StackTrace.ToString(), "AODXServer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void appendTxtLogSafe(string txt)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action(() => txtLog.AppendText(txt)));
                return;
            }
            txtLog.AppendText(txt);
        }

        private void appendLstUsersSafe(string txt)
        {
            if (lstUsers.InvokeRequired)
            {
                lstUsers.Invoke(new Action(() => lstUsers.Items.Add(txt)));
                return;
            }
            lstUsers.Items.Add(txt);
        }

        private void removeLstUsersSafe(string txt)
        {
            if (lstUsers.InvokeRequired)
            {
                lstUsers.Invoke(new Action(() => lstUsers.Items.Remove(txt)));
                return;
            }
            lstUsers.Items.Remove(txt);
        }

        public void OnSend(IAsyncResult ar)
        {
            try
            {
                Socket client = (Socket)ar.AsyncState;
                client.EndSend(ar);
            }
            catch (SocketException)
            { }
            catch (ObjectDisposedException)
            { }
            catch (Exception ex)
            {
                if (Program.debug)
                    MessageBox.Show(ex.Message + "\r\n" + ex.StackTrace.ToString(), "AODXServer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void OnSendClose(IAsyncResult ar)
        {
            try
            {
                masterSocket.EndSend(ar);
                masterSocket.Disconnect(false);
                int nIndex = 0;
                foreach (ClientInfo client in clientList)
                {
                    clientList.RemoveAt(nIndex);
                    //removeLstUsersSafe(client.strName + " - " + client.character);
                    removeLstUsersSafe(client.strName + " - " + ((IPEndPoint)client.socket.RemoteEndPoint).Address.ToString());
                    client.socket.Close();
                    ++nIndex;
                }
                //serverSocket.EndReceive();
                serverSocket.Close();
                isClosing = true;
            }
            catch (SocketException)
            { }
            catch (ObjectDisposedException)
            { }
            catch (Exception ex)
            {
                if (Program.debug)
                    MessageBox.Show(ex.Message + "\r\n" + ex.StackTrace.ToString(), "AODXServer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void updateMenuItem_Click(object sender, EventArgs e)
        {
            updateCheck();
        }

        private void updateCheck(bool silent = false)
        {
            // in newVersion variable we will store the  
            // version info from xml file  
            Version newVersion = null;
            // and in this variable we will put the url we  
            // would like to open so that the user can  
            // download the new version  
            // it can be a homepage or a direct  
            // link to zip/exe file  
            string url = "";
            XmlTextReader reader;
            try
            {
                // provide the XmlTextReader with the URL of  
                // our xml document  
                string xmlURL = "https://raw.githubusercontent.com/jpmac26/AODX/master/version.xml";
                reader = new XmlTextReader(xmlURL);
                // simply (and easily) skip the junk at the beginning  
                reader.MoveToContent();
                // internal - as the XmlTextReader moves only  
                // forward, we save current xml element name  
                // in elementName variable. When we parse a  
                // text node, we refer to elementName to check  
                // what was the node name  
                string elementName = "";
                // we check if the xml starts with a proper  
                // "AODX" element node  
                if ((reader.NodeType == XmlNodeType.Element) && (reader.Name == "AODX"))
                {
                    bool done = false;
                    while (reader.Read() && !done)
                    {
                        // when we find an element node,  
                        // we remember its name  
                        if (reader.NodeType == XmlNodeType.Element)
                            elementName = reader.Name;
                        else
                        {
                            if (elementName == "Server")
                            {
                                bool done2 = false;
                                while (reader.Read() && !done2)
                                {
                                    if (reader.NodeType == XmlNodeType.Element)
                                        elementName = reader.Name;
                                    // for text nodes...  
                                    else if ((reader.NodeType == XmlNodeType.Text) && (reader.HasValue))
                                    {
                                        // we check what the name of the node was  
                                        switch (elementName)
                                        {
                                            case "version":
                                                // thats why we keep the version info  
                                                // in xxx.xxx.xxx.xxx format  
                                                // the Version class does the  
                                                // parsing for us  
                                                newVersion = new Version(reader.Value);
                                                break;
                                            case "url":
                                                url = reader.Value;
                                                done2 = true;
                                                done = true;
                                                break;
                                        }
                                    }
                                }
                                break;
                            }
                        }
                    }
                }
                if (reader != null)
                    reader.Close();
            }
            catch (Exception)
            {

            }

            // get the running version  
            Version curVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            // compare the versions  
            if (curVersion.CompareTo(newVersion) < 0)
            {
                // ask the user if he would like  
                // to download the new version  
                string title = "Update Check";
                string question = "New server version available: " + newVersion.ToString() + ".\r\n (You have version " + curVersion.ToString() + "). \r\n Download the new version?";
                if (MessageBox.Show(this, question, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    // navigate the default web  
                    // browser to our app  
                    // homepage (the url  
                    // comes from the xml content)  
                    System.Diagnostics.Process.Start(url);
                }
            }
            else if (!silent)
                MessageBox.Show(this, "You have the latest version: " + curVersion.ToString(), "Update Check", MessageBoxButtons.OK);
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void settingsMenuItem_Click(object sender, EventArgs e)
        {
            var SettingsWindow = new SettingsForm();
            SettingsWindow.Show();
        }

        private void kickToolStripMenuItem_Click(object sender, EventArgs e)
        {
            kickUser();
        }

        private void banToolStripMenuItem_Click(object sender, EventArgs e)
        {
            banUser(); //This needs to happen first, because lstUsers.SelectedItem will be null after we kick the user
            kickUser(true);
        }

        private void kickUser(bool silent = false)
        {
            if (lstUsers.SelectedItem != null)
            {
                string username = ((string)lstUsers.SelectedItem).Split(new string[] { " - " }, StringSplitOptions.None)[0];
                int nIndex = 0;
                foreach (ClientInfo client in clientList)
                {
                    if (client.strName == username)
                    {
                        clientList.RemoveAt(nIndex);
                        //removeLstUsersSafe(client.strName + " - " + client.character);
                        removeLstUsersSafe(client.strName + " - " + ((IPEndPoint)client.socket.RemoteEndPoint).Address.ToString());
                        client.socket.Close();
                        break;
                    }
                    ++nIndex;
                }

                if (!silent)
                {
                    Data msgToSend = new Data();
                    msgToSend.cmdCommand = Command.Logout;
                    msgToSend.strMessage = "<<<" + username + " has been kicked from the courtroom>>>";
                    byte[] msg = msgToSend.ToByte();

                    if (clientList.Count > 0)
                    {
                        foreach (ClientInfo clientInfo in clientList)
                        {
                            //Send the message to all users
                            clientInfo.socket.BeginSend(msg, 0, msg.Length, SocketFlags.None, new AsyncCallback(OnSend), clientInfo.socket);
                        }
                    }

                    appendTxtLogSafe(msgToSend.strMessage + "\r\n");
                }

                userNumStat.Text = "Users Online: " + clientList.Count;
            }
        }

        private void banUser()
        {
            if (lstUsers.SelectedItem != null)
            {
                string IP = ((string)lstUsers.SelectedItem).Split(new string[] { " - " }, StringSplitOptions.None)[1];
                int nIndex = 0;
                foreach (ClientInfo client in clientList)
                {
                    if (client.socket.RemoteEndPoint.ToString().Split(':')[0] == IP)
                    {
                        if (!File.Exists("base/banlist.ini"))
                        {
                            File.CreateText("base/banlist.ini");
                            using (StreamWriter w = new StreamWriter("base/banlist.ini"))
                            {
                                w.WriteLine("[banlist]\r\n");
                            }
                        }

                        bool found = false;
                        using (StreamReader r = new StreamReader("base/banlist.ini"))
                        {
                            while (!r.EndOfStream)
                            {
                                if (r.ReadLine().StartsWith(IP))
                                {
                                    found = true;
                                    break;
                                }

                            }
                        }

                        if (found == false)
                        {
                            using (StreamWriter file = new StreamWriter("base/banlist.ini", true))
                            {
                                file.WriteLine(IP);
                            }
                        }
                        break;
                    }
                    ++nIndex;
                }

                Data msgToSend = new Data();
                msgToSend.cmdCommand = Command.Logout;
                msgToSend.strMessage = "<<<" + ((string)lstUsers.SelectedItem).Split(new string[] { " - " }, StringSplitOptions.None)[0] + " has been banned from the courtroom>>>";
                byte[] msg = msgToSend.ToByte();

                if (clientList.Count > 0)
                {
                    foreach (ClientInfo clientInfo in clientList)
                    {
                        if (clientInfo.socket.RemoteEndPoint.ToString().Split(':')[0] != IP)
                        {
                            //Send the message to all users
                            clientInfo.socket.BeginSend(msg, 0, msg.Length, SocketFlags.None, new AsyncCallback(OnSend), clientInfo.socket);
                        }
                    }
                }

                appendTxtLogSafe(msgToSend.strMessage + "\r\n");
                userNumStat.Text = "Users Online: " + clientList.Count;
            }
        }
    }

    //The data structure by which the server and the client interact with 
    //each other
    class Data
    {
        //Default constructor
        public Data()
        {
            cmdCommand = Command.Null;
            strMessage = null;
            strName = null;
            anim = 1;
            callout = 0;
            textColor = Color.PeachPuff;
        }

        //Converts the bytes into an object of type Data
        public Data(byte[] data)
        {
            //The first four bytes are for the Command
            cmdCommand = (Command)BitConverter.ToInt32(data, 0);

            //The next four store the length of the name
            int nameLen = BitConverter.ToInt32(data, 4);

            anim = BitConverter.ToInt32(data, 8);

            callout = data[12];

            int textColorLen = BitConverter.ToInt32(data, 13);

            //The next four store the length of the message
            int msgLen = BitConverter.ToInt32(data, 17);

            //This check makes sure that strName has been passed in the array of bytes
            if (nameLen > 0)
                strName = Encoding.UTF8.GetString(data, 21, nameLen);
            else
                strName = null;

            if (textColorLen > 0)
                textColor = Color.FromArgb(BitConverter.ToInt32(data, 21 + nameLen));
            else
                textColor = Color.White;

            //This checks for a null message field
            if (msgLen > 0)
                strMessage = Encoding.UTF8.GetString(data, 21 + nameLen + textColorLen, msgLen);
            else
                strMessage = null;
        }

        //Converts the Data structure into an array of bytes
        public byte[] ToByte(bool appendExtra = false)
        {
            List<byte> result = new List<byte>();

            //First four are for the Command
            result.AddRange(BitConverter.GetBytes((int)cmdCommand));

            //Add the length of the name
            if (strName != null)
                result.AddRange(BitConverter.GetBytes(strName.Length));
            else
                result.AddRange(BitConverter.GetBytes(0));

            result.AddRange(BitConverter.GetBytes(anim));

            result.Add(callout);

            //Add the color length
            result.AddRange(BitConverter.GetBytes(4));

            //Length of the message
            if (strMessage != null)
                result.AddRange(BitConverter.GetBytes(strMessage.Length));
            else
                result.AddRange(BitConverter.GetBytes(0));

            //Add the name
            if (strName != null)
                result.AddRange(Encoding.UTF8.GetBytes(strName));

            //if (textColor != Color.PeachPuff)
            result.AddRange(BitConverter.GetBytes(textColor.ToArgb()));

            //And, lastly we add the message text to our array of bytes
            if (strMessage != null)
                result.AddRange(Encoding.UTF8.GetBytes(strMessage));

            if (appendExtra == true)
                result.Add(1);
            else
                result.Add(0);

            if (result[0] == 4)
            {
                if (result[0] == 4)
                {

                }
            }

            return result.ToArray();
        }

        public string strName;      //Name by which the client logs into the room
        public int anim;
        public byte callout;
        public Color textColor;
        public string strMessage;   //Message text
        public Command cmdCommand;  //Command type (login, logout, send message, etcetera)
    }

    //The data structure by which the server and the client interact with 
    //each other
    class EviData
    {
        //Default constructor
        public EviData(string name, string desc, string note, Image icon, int i)
        {
            cmdCommand = Command.Evidence;
            strName = name;
            strDesc = desc;
            strNote = note;
            index = i;
            MemoryStream ms = new MemoryStream();
            icon.Save(ms, icon.RawFormat);
            using (FileStream fs = new FileStream("base/test.gif", FileMode.Create))
            {
                fs.Write(ms.ToArray(), 0, ms.ToArray().Length);
            }

            icon.Save("base/test2.gif");

            //System.Drawing.Imaging.ImageFormat.Gif.Guid
            dataBytes = ms.ToArray();
        }

        //Converts the bytes into an object of type Data
        public EviData(byte[] data)
        {
            //The first four bytes are for the Command
            cmdCommand = (Command)BitConverter.ToInt32(data, 0);

            index = BitConverter.ToInt32(data, 4);

            //The next four store the length of the name
            int nameLen = BitConverter.ToInt32(data, 8);

            int descLen = BitConverter.ToInt32(data, 12);

            int noteLen = BitConverter.ToInt32(data, 16);

            dataSize = BitConverter.ToInt32(data, 20);

            //This check makes sure that strName has been passed in the array of bytes
            if (nameLen > 0)
                strName = Encoding.UTF8.GetString(data, 24, nameLen);
            else
                strName = null;

            if (descLen > 0)
                strDesc = Encoding.UTF8.GetString(data, 24 + nameLen, descLen);
            else
                strDesc = null;

            if (noteLen > 0)
                strNote = Encoding.UTF8.GetString(data, 24 + nameLen + descLen, noteLen);
            else
                strNote = null;

            if (dataSize > 0)
                dataBytes = data.Skip(24 + nameLen + descLen + noteLen).ToArray();
            else
                dataBytes = null;
        }

        //Converts the Data structure into an array of bytes
        public byte[] ToByte()
        {
            List<byte> result = new List<byte>();

            //First four are for the Command
            result.AddRange(BitConverter.GetBytes((int)cmdCommand));

            result.AddRange(BitConverter.GetBytes(index));

            //Add the length of the name
            if (strName != null)
                result.AddRange(BitConverter.GetBytes(strName.Length));

            if (strDesc != null)
                result.AddRange(BitConverter.GetBytes(strDesc.Length));

            if (strNote != null)
                result.AddRange(BitConverter.GetBytes(strNote.Length));
            else
                result.AddRange(BitConverter.GetBytes("Submitted by the judge.".Length));

            if (dataBytes != null)
                result.AddRange(BitConverter.GetBytes(dataBytes.Length));

            //Add the name
            if (strName != null)
                result.AddRange(Encoding.UTF8.GetBytes(strName));

            if (strDesc != null)
                result.AddRange(Encoding.UTF8.GetBytes(strDesc));

            result.AddRange(Encoding.UTF8.GetBytes(strNote ?? "Submitted by the judge."));

            result.AddRange(dataBytes);

            return result.ToArray();
        }

        public string strName;      //Name and path of the file being sent/received
        public string strDesc;
        public string strNote;
        public int index;
        public int dataSize;
        public byte[] dataBytes;
        public Command cmdCommand;  //Command type (login, logout, send message, etcetera)
    }

    public class Evidence
    {
        public string name;
        public string filename;
        public string desc;
        public string note;
        public int index;
        public Image icon;
    }
}