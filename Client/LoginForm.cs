using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml;

namespace Client
{
	public partial class LoginForm : Form
	{
		private readonly AboutBox AboutForm = new AboutBox();
		private byte[] byteData = new byte[4194304];
		public List<string> charList = new List<string>();
		public Socket clientSocket;
		public List<Evidence> eviList = new List<Evidence>();
		private bool favorites;
		private int incomingSize;
		private string masterserverIP;
		public Socket masterSocket;
		public List<string> musicList = new List<string>();
		private readonly Dictionary<string, string> serverData = new Dictionary<string, string>(); // Address, Name

		public LoginForm()
		{
			InitializeComponent();
			btn_PublicServers.Load("base/misc/btn_public_on.png");
			background.Controls.Add(btn_PublicServers);
			btn_FavoriteServers.Load("base/misc/btn_fav_off.png");
			background.Controls.Add(btn_FavoriteServers);
			btn_Refresh.Load("base/misc/btn_refresh.png");
			background.Controls.Add(btn_Refresh);
			btn_AddFav.Load("base/misc/btn_addFav.png");
			background.Controls.Add(btn_AddFav);
			btn_Connect.Load("base/misc/btn_connect.png");
			background.Controls.Add(btn_Connect);
			userCount.BackColor = Color.Transparent;
			background.Controls.Add(userCount);
			serverDescTextBox.BackColor = Color.Transparent;
			background.Controls.Add(serverDescTextBox);
		}

		private void LoginForm_Load(object sender, EventArgs e)
		{
			updateCheck(true);
			CheckForIllegalCrossThreadCalls = false;
			serverDescTextBox.Text = "";
			masterserverIP = iniParser.GetMasterIP();
			if (masterserverIP == null)
			{
				MessageBox.Show(
					"Failed to find masterserver IP in " + '"' + "masterserver.ini" + '"' +
					". \r\n Assuming masterserver is locally hosted.", "AODXClient", MessageBoxButtons.OK);
				masterserverIP = "127.0.0.1";
			}
			ConnectToMasterServer();
		}

		private void LoginForm_FormClosing(object sender, FormClosingEventArgs e)
		{
			if (DialogResult != DialogResult.OK)
			{
				var b = new byte[1];
				b[0] = 103;

				//Send the message to the server
				if (clientSocket != null && clientSocket.Connected)
					clientSocket.BeginSend(b, 0, b.Length, SocketFlags.None, OnSendClose, null);

				if (Directory.Exists("base/cases"))
					Directory.Delete("base/cases", true);
			}
		}

		private void ConnectToMasterServer()
		{
			masterSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

			var ipAddress = IPAddress.Parse(masterserverIP);

			//Masterserver is listening on port 1002
			var ipEndPoint = new IPEndPoint(ipAddress, 1002);

			//Connect to the masterserver
			masterSocket.BeginConnect(ipEndPoint, OnConnectMaster, null);
		}

		private void ConnectToServer(string ip)
		{
			clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

			var ipAddress = IPAddress.Parse(ip.Split(':')[0]);

			//Servers usually listen on port 1000, but just in case...
			//IPEndPoint ipEndPoint = new IPEndPoint(ipAddress, Convert.ToInt32(ip.Split(':')[1]));
			var ipEndPoint = new IPEndPoint(ipAddress, 1000);

			//Connect to the server
			clientSocket.BeginConnect(ipEndPoint, OnConnect, null);
		}

		private void OnConnect(IAsyncResult ar)
		{
			try
			{
				clientSocket.EndConnect(ar);
				//btnOK.Enabled = false;

				//We are connected, so request the description and user count from the server
				clientSocket.BeginReceive(byteData, 0, byteData.Length, SocketFlags.None, OnReceiveServerInfo, null);
			} catch (Exception ex)
			{
				if (Program.debug)
					MessageBox.Show(ex.Message + "\r\n" + ex.StackTrace, "AODXClient", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void OnConnectMaster(IAsyncResult ar)
		{
			try
			{
				masterSocket.EndConnect(ar);
				btn_Refresh.Load("base/misc/btn_refresh.png");
				//btnOK.Enabled = false;

				var b = new byte[1];
				b[0] = 102;

				//Send the message to the server
				masterSocket.BeginSend(b, 0, b.Length, SocketFlags.None, OnSendMaster, null);

				masterSocket.BeginReceive(byteData, 0, byteData.Length, SocketFlags.None, OnReceiveServerList, null);
			} catch (Exception ex)
			{
				if (Program.debug)
					MessageBox.Show("Unable to connect to the masterserver:\r\n" + ex.Message + "\r\n" + ex.StackTrace, "AODXClient",
						MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void OnSend(IAsyncResult ar)
		{
			try
			{
				clientSocket.EndSend(ar);
			} catch (SocketException)
			{
				if (MessageBox.Show("You have been kicked from the server.", "AODXClient", MessageBoxButtons.OK) == DialogResult.OK |
					MessageBox.Show("You have been kicked from the server.", "AODXClient", MessageBoxButtons.OK) ==
					DialogResult.Cancel)
					Close();
			} catch (ObjectDisposedException)
			{
			} catch (Exception ex)
			{
				if (Program.debug)
					MessageBox.Show(ex.Message + "\r\n" + ex.StackTrace, "AODXClient", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void OnSendClose(IAsyncResult ar)
		{
			try
			{
				clientSocket.EndSend(ar);
				clientSocket.Close();
			} catch (SocketException)
			{
			} catch (ObjectDisposedException)
			{
			} catch (Exception ex)
			{
				if (Program.debug)
					MessageBox.Show(ex.Message + "\r\n" + ex.StackTrace, "AODXClient", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void OnSendMaster(IAsyncResult ar)
		{
			try
			{
				masterSocket.EndSend(ar);
			} catch (Exception ex)
			{
				if (Program.debug)
					MessageBox.Show(ex.Message + "\r\n" + ex.StackTrace, "AODXClient", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void OnReceive(IAsyncResult ar)
		{
			try
			{
				while (byteData[0] == 4 && byteData.Length < incomingSize)
				{
					//string test = "";
				}

				clientSocket.EndReceive(ar);

				if (byteData[0] == 8)
				{
					var msg = new EviData(byteData);

					var evi = new Evidence();
					evi.name = msg.strName;
					evi.desc = msg.strDesc;
					evi.note = msg.strNote;
					evi.index = msg.index;

					using (var ms = new MemoryStream(msg.dataBytes))
					{
						evi.icon = Image.FromStream(ms, false, true);
					}

					eviList.Add(evi);

					//string dirName = "";
					//for (int x = 0; x < msg.strName.Split('/').Length - 1; x++)
					//{
					//    dirName = dirName + msg.strName.Split('/')[x];
					//    if (x < msg.strName.Split('/').Length - 2)
					//        dirName = dirName + '/';
					//}
					//if (!Directory.Exists(dirName))
					//    Directory.CreateDirectory(dirName);

					//using (FileStream fs = new FileStream(msg.strName, FileMode.Create))
					//{
					//    using (BinaryWriter w = new BinaryWriter(fs))
					//    {
					//        if (msg.dataSize > 0)
					//            w.Write(msg.dataBytes.Take(msg.dataSize).ToArray());
					//    }
					//}

					clientSocket.BeginReceive(byteData, 0, byteData.Length, SocketFlags.None, OnReceive, null);
				} else
				{
					var msgReceived = new Data(byteData);

					if (msgReceived.cmdCommand == Command.Disconnect)
					{
						MessageBox.Show("You are banned from this server!");
						btn_Connect.Image = Image.FromFile("base/misc/btn_connect.png");
						//clientSocket.Close();
						//Close();
					} else if (msgReceived.cmdCommand == Command.PacketSize)
					{
						incomingSize = Convert.ToInt32(msgReceived.strMessage);
						byteData = new byte[incomingSize];

						//DialogResult = DialogResult.OK;
						var msgToSend = new Data();
						msgToSend.cmdCommand = Command.DataInfo;

						var b = msgToSend.ToByte();

						//Send the message to the server
						clientSocket.BeginSend(b, 0, b.Length, SocketFlags.None, OnSend, null);

						clientSocket.BeginReceive(byteData, 0, byteData.Length, SocketFlags.None, OnReceive, null);
						//Close();
					} else if (msgReceived.cmdCommand == Command.DataInfo)
					{
						if (msgReceived.strMessage != null && msgReceived.strMessage != "")
						{
							var data = msgReceived.strMessage.Split('|');
							var charCount = Convert.ToInt32(data[0]);
							if (charCount > 0)
							{
								for (var x = 1; x < charCount; x++)
								{
									charList.Add(data[x]);
								}
							}

							var songCount = Convert.ToInt32(data[charCount + 1]);
							if (songCount > 0)
							{
								for (var x = charCount + 2; x < charCount + 2 + songCount; x++)
								{
									musicList.Add(data[x]);
								}
							}
						}

						byteData = new byte[4194304];
						//Do stuff with the evidence/extra binary data here
					} else
						clientSocket.BeginReceive(byteData, 0, byteData.Length, SocketFlags.None, OnReceive, null);

					if (msgReceived.cmdCommand == Command.DataInfo)
					{
						//Program.charList = charList;
						//Program.musicList = musicList;
						//Program.connection = clientSocket;
						DialogResult = DialogResult.OK;
						Close();
					}
				}
			} catch (SocketException)
			{
				if (MessageBox.Show("You have been disconnected from the server.", "AODXClient", MessageBoxButtons.OK) ==
					DialogResult.OK |
					MessageBox.Show("You have been kicked from the server.", "AODXClient", MessageBoxButtons.OK) ==
					DialogResult.Cancel)
					Close();
			} catch (ObjectDisposedException)
			{
			} catch (Exception ex)
			{
				if (Program.debug)
					MessageBox.Show(ex.Message + "\r\n" + ex.StackTrace, "AODXClient", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void OnReceiveServerInfo(IAsyncResult ar)
		{
			try
			{
				clientSocket.EndReceive(ar);

				if (byteData[0] == 101) //server
				{
					var len = BitConverter.ToInt32(byteData, 1);
					var infoString = Encoding.UTF8.GetString(byteData, 5, len);
					editServerDescTB(infoString.Split('|')[1]);
					userCount.Text = "Users: " + Convert.ToInt32(infoString.Split('|')[11]);

					//clientSocket.BeginReceive(byteData, 0, byteData.Length, SocketFlags.None, new AsyncCallback(OnReceive), clientSocket);
				}
			} catch (ObjectDisposedException)
			{
			} catch (Exception ex)
			{
				if (Program.debug)
					MessageBox.Show(ex.Message + "\r\n" + ex.StackTrace, "AODXClient", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void OnReceiveServerList(IAsyncResult ar)
		{
			try
			{
				serverData.Clear();
				serverList.ClearSelected();
				masterSocket.EndReceive(ar);
				var strLen = BitConverter.ToInt32(byteData, 0);
				var allServerData = Encoding.UTF8.GetString(byteData, 4, strLen);
				var servers = allServerData.Split(new[] { "/" }, StringSplitOptions.RemoveEmptyEntries);
				foreach (var server in servers)
				{
					//serverList.Items.Add(server.Split('|')[0]); // + server.Split('|')[2] + server.Split('|')[3]);
					serverData.Add(server.Split('|')[2], server.Split('|')[0]);
					//editServerDescTB(server.Split('|')[1]);
				}
				if (serverData.Count > 0)
				{
					serverList.DataSource = new BindingSource(serverData, null);
					serverList.DisplayMember = "Value";
					serverList.ValueMember = "Key";
				} else
				{
					serverList.DataSource = null;
					serverList.DisplayMember = null;
					serverList.ValueMember = null;
					serverList.Items.Clear();
				}
				masterSocket.Close();
			} catch (ObjectDisposedException)
			{
			} catch (Exception ex)
			{
				if (Program.debug)
					MessageBox.Show(ex.Message + "\r\n" + ex.StackTrace, "AODXClient", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void editServerDescTB(string txt)
		{
			if (serverDescTextBox.InvokeRequired)
			{
				serverDescTextBox.Invoke(new Action(() => serverDescTextBox.Text = txt));
				return;
			}
			serverDescTextBox.Text = txt;
		}

		private void btn_PublicServers_Click(object sender, EventArgs e)
		{
			if (favorites)
			{
				favorites = false;
				btn_PublicServers.Load("base/misc/btn_public_on.png");
				btn_FavoriteServers.Load("base/misc/btn_fav_off.png");
			}
		}

		private void btn_FavoriteServers_Click(object sender, EventArgs e)
		{
			if (favorites == false)
			{
				favorites = true;
				btn_PublicServers.Load("base/misc/btn_public_off.png");
				btn_FavoriteServers.Load("base/misc/btn_fav_on.png");
			}
		}

		private void btn_Refresh_Click(object sender, EventArgs e)
		{
			btn_Refresh.Load("base/misc/btn_refresh_pressed.png");
			if (clientSocket != null && clientSocket.Connected)
			{
				//clientSocket.BeginDisconnect(true, new AsyncCallback(OnDisconnect), null);
				//clientSocket.Close();
			}
			ConnectToMasterServer();
		}

		private void btn_AddFav_Click(object sender, EventArgs e)
		{
		}

		private void btn_Connect_Click(object sender, EventArgs e)
		{
			btn_Connect.Load("base/misc/btn_connect_pressed.png");
			try
			{
				if (serverList.Items.Count > 0 && serverList.SelectedItem != null && clientSocket != null)
				{
					var msgToSend = new Data();
					msgToSend.cmdCommand = Command.PacketSize;

					var message = msgToSend.ToByte();

					clientSocket.BeginSend(message, 0, message.Length, SocketFlags.None, OnSend, null);

					clientSocket.BeginReceive(byteData, 0, byteData.Length, SocketFlags.None, OnReceive, null);
				}
			} catch (Exception ex)
			{
				if (Program.debug)
					MessageBox.Show(ex.Message + "\r\n" + ex.StackTrace, "AODXClient", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void serverList_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (clientSocket != null && clientSocket.Connected)
			{
				//byte[] b = new byte[1];
				//b[0] = 103;

				//Send the message to the server
				//clientSocket.BeginSend(b, 0, b.Length, SocketFlags.None, new AsyncCallback(OnSend), null);

				//clientSocket.BeginDisconnect(true, new AsyncCallback(OnDisconnect), null);
				//clientSocket.Close();
			}

			if (serverList.Items.Count > 0 && serverList.SelectedItem != null)
			{
				if (((KeyValuePair<string, string>)serverList.SelectedItem).Key.Split(':')[0] !=
					clientSocket?.RemoteEndPoint.ToString().Split(':')[0])
					ConnectToServer(((KeyValuePair<string, string>)serverList.SelectedItem).Key.Split(',')[0]);
			}
		}

		private void aboutMenuItem_Click(object sender, EventArgs e)
		{
			AboutForm.Show();
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
			var url = "";
			XmlTextReader reader;
			try
			{
				// provide the XmlTextReader with the URL of  
				// our xml document  
				var xmlURL = "https://raw.githubusercontent.com/jpmac26/AODX/master/version.xml";
				reader = new XmlTextReader(xmlURL);
				// simply (and easily) skip the junk at the beginning  
				reader.MoveToContent();
				// internal - as the XmlTextReader moves only  
				// forward, we save current xml element name  
				// in elementName variable. When we parse a  
				// text node, we refer to elementName to check  
				// what was the node name  
				var elementName = "";
				// we check if the xml starts with a proper  
				// "AODX" element node  
				if ((reader.NodeType == XmlNodeType.Element) && (reader.Name == "AODX"))
				{
					var done = false;
					while (reader.Read() && !done)
					{
						// when we find an element node,  
						// we remember its name  
						if (reader.NodeType == XmlNodeType.Element)
							elementName = reader.Name;
						else
						{
							if (elementName == "Client")
							{
								var done2 = false;
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
			} catch (Exception)
			{
			}

			// get the running version  
			var curVersion = Assembly.GetExecutingAssembly().GetName().Version;
			// compare the versions  
			if (curVersion.CompareTo(newVersion) < 0)
			{
				// ask the user if he would like  
				// to download the new version  
				var title = "Update Check";
				var question = "New client version available: " + newVersion + ".\r\n (You have version " + curVersion +
							   "). \r\n Download the new version?";
				if (MessageBox.Show(this, question, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
				{
					// navigate the default web  
					// browser to our app  
					// homepage (the url  
					// comes from the xml content)  
					Process.Start(url);
				}
			} else if (!silent)
				MessageBox.Show(this, "You have the latest version: " + curVersion, "Update Check", MessageBoxButtons.OK);
		}

		private void exitMenuItem_Click(object sender, EventArgs e)
		{
			Close();
		}
	}
}