/********************************************************************************

	De-Ops: Decentralized Operations
	Copyright (C) 2006 John Marshall Group, Inc.

	By contributing code you grant John Marshall Group an unlimited, non-exclusive
	license to your contribution.

	For support, questions, commercial use, etc...
	E-Mail: swabby@c0re.net

********************************************************************************/

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Security.Cryptography;

using DeOps.Implementation.Dht;
using DeOps.Implementation.Protocol;
using DeOps.Implementation.Protocol.Net;
using DeOps.Simulator;

namespace DeOps.Implementation.Transport
{
	/// <summary>
	/// Summary description for UdpHandler.
	/// </summary>
	internal class UdpHandler
	{
        // super-class
        OpCore Core;
		DhtNetwork Network;

		Socket UdpSocket;

		internal ushort ListenPort;
	
		byte[] ReceiveBuff = new byte[4096];
		byte[] InflateBuff = new byte[4096];

		BufferData SendData = new BufferData( new byte[4096] );

		ASCIIEncoding StringEnc = new ASCIIEncoding();

        const int MAX_UDP_SIZE = 1500;


        internal UdpHandler(DhtNetwork network)
		{
            Network = network;
            Core = network.Core;

            ListenPort = Network.IsGlobal ? Core.User.Settings.GlobalPortUdp : Core.User.Settings.OpPortUdp;
            
            if (Core.Sim != null)
                return;

			UdpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

			// listen
			bool bound    = false;
			int  attempts = 0;
			while( !bound && attempts < 5)
			{
				try
				{
					UdpSocket.Bind( new IPEndPoint( System.Net.IPAddress.Any, ListenPort) );
					bound = true;
					
					EndPoint tempSender = (EndPoint) new IPEndPoint(IPAddress.Any, 0);
					UdpSocket.BeginReceiveFrom(ReceiveBuff, 0, ReceiveBuff.Length, SocketFlags.None, ref tempSender, new AsyncCallback(UdpSocket_Receive), UdpSocket);
				
					Network.UpdateLog("Network", "Listening for UDP on port " + ListenPort.ToString());

				}
				catch(Exception ex)
				{ 
					Network.UpdateLog("Exception", "UdpHandler::UdpHandler: " + ex.Message);
			
					attempts++; 
					ListenPort++;
				}
			}
		}

		internal void Shutdown()
		{
			try
			{
				Socket oldSocket = UdpSocket; // do this to prevent listen exception
				UdpSocket = null;

				if(oldSocket != null)
					oldSocket.Close();
			}
			catch(Exception ex)
			{
				Network.UpdateLog("Exception", "UdpHandler::Shudown: " + ex.Message);
			}
		}

		internal void SendTo(DhtAddress address, G2Packet packet)
		{
            if (packet is NetworkPacket)
                ((NetworkPacket)packet).SourceID = Core.LocalDhtID;

            byte[] encoded = packet.Encode(Core.Protocol);

            PacketLogEntry logEntry = new PacketLogEntry(TransportProtocol.Udp, DirectionType.Out, address, encoded);
            Network.LogPacket(logEntry);

            byte[] final = null;

            // encrypt, turn off encryption during simulation
            if (Core.Sim == null || Core.Sim.Internet.TestEncryption)
            {
                lock (Network.AugmentedCrypt)
                {
                    BitConverter.GetBytes(address.DhtID).CopyTo(Network.AugmentedCrypt.Key, 0);

                    final = Utilities.EncryptBytes(encoded, Network.AugmentedCrypt);
                }
            }
            else
                final = encoded;

            // send
            try
			{
                if (Core.Sim != null)
                {
                    Core.Sim.Internet.SendPacket(SimPacketType.Udp, Network, final, address.ToEndPoint(), null);
                    return;
                }

                if (UdpSocket == null)
                    return;

                if (encoded.Length> MAX_UDP_SIZE)
					throw new Exception("Packet larger than " + MAX_UDP_SIZE.ToString() + " bytes");

                UdpSocket.BeginSendTo(final, 0, final.Length, SocketFlags.None, address.ToEndPoint(), new AsyncCallback(UdpSocket_SendTo), UdpSocket);
			}
			catch(Exception ex)
			{ 
				Network.UpdateLog("Exception", "UdpHandler::SendTo: " + ex.Message);
			}
		}

		void UdpSocket_SendTo(IAsyncResult asyncResult)
		{
			if(UdpSocket == null)
				return;

			try
			{
				int bytesSent = UdpSocket.EndSendTo(asyncResult);
			}
			catch(Exception ex)
			{ 
				Network.UpdateLog("Exception", "UdpHandler::UdpSocket_SendTo: " + ex.Message);
			}
		}

		void UdpSocket_Receive (IAsyncResult asyncResult)
		{
			if(UdpSocket == null)
				return;
		
			try
			{
				EndPoint sender = (EndPoint) new IPEndPoint(IPAddress.Any, 0);
				int recvLen = UdpSocket.EndReceiveFrom(asyncResult, ref sender);

                OnReceive(ReceiveBuff, recvLen, (IPEndPoint)sender);
			}
			catch(Exception ex)
			{ 
				Network.UpdateLog("Exception", "UdpHandler::UdpSocket_Receive:1: " + ex.Message);
			}
			

            //crit
			// calling a sendto to a good host but unreachable port causes exceptions that stack the more sentto's you call
			// endreceivefrom will throw and so will begin until begin has been called enough to makeup for the unreachable hosts
			// if this loop is exited without a successful call to beginreceive from, inbound udb is game over
			int attempts = 0;
			while(attempts < 100)
			{
				try
				{
					EndPoint sender = (EndPoint) new IPEndPoint(IPAddress.Any, 0);
					UdpSocket.BeginReceiveFrom(ReceiveBuff, 0, ReceiveBuff.Length, SocketFlags.None, ref sender, new AsyncCallback(UdpSocket_Receive), UdpSocket);
					break;
				}
				catch(Exception ex)
				{ 
					Network.UpdateLog("Exception", "UdpHandler::UdpSocket_Receive:2: " + ex.Message + ", attempt " + attempts.ToString());
					attempts++;
				}
			}
		}

        internal void OnReceive(byte[] buff, int length, IPEndPoint sender)
        {
            byte[] finalBuff = buff;

            if (Core.Sim == null || Core.Sim.Internet.TestEncryption) // turn off encryption during simulation
            {
                if (length < Network.AugmentedCrypt.IV.Length)
                    throw new Exception("Not enough data received for IV");

                lock (Network.AugmentedCrypt)
                {
                    BitConverter.GetBytes(Core.LocalDhtID).CopyTo(Network.AugmentedCrypt.Key, 0);

                    finalBuff = Utilities.DecryptBytes(buff, length, Network.AugmentedCrypt);
                    length = finalBuff.Length;
                }
            }

            ParsePacket(finalBuff, length, sender);
        }

		void ParsePacket(byte[] buff, int length, IPEndPoint sender)
		{
			G2ReceivedPacket packet = new G2ReceivedPacket();
            packet.Root = new G2Header(buff);

            if(Core.Protocol.ReadPacket(packet.Root))
            {
                packet.Source = new DhtAddress(0, sender.Address, (ushort)sender.Port);

                PacketLogEntry logEntry = new PacketLogEntry(TransportProtocol.Udp, DirectionType.In, packet.Source, Utilities.ExtractBytes(packet.Root.Data, packet.Root.PacketPos, packet.Root.PacketSize));
				Network.LogPacket(logEntry);

				try
				{
					Network.ReceivePacket(packet);
				}
				catch(Exception ex)
				{ 
					Network.UpdateLog("Exception", "UdpHandler::ParsePacket: " + ex.Message);
				}
			}
		}
	}
}
