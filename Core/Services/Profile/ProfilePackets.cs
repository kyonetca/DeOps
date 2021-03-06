using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

using DeOps.Implementation.Protocol;


namespace DeOps.Services.Profile
{
    public class ProfilePacket
    {
        public const byte Attachment = 0x10;
        public const byte Field = 0x20;
    }

    public class ProfileAttachment : G2Packet
    {
        const byte Packet_Name = 0x10;
        const byte Packet_Size = 0x20;

        public string Name;
        public long Size;


        public ProfileAttachment()
        {
        }

        public ProfileAttachment(string name, long size)
        {
            Name = name;
            Size = size;
        }

        public override byte[] Encode(G2Protocol protocol)
        {
            lock (protocol.WriteSection)
            {
                G2Frame header = protocol.WritePacket(null, ProfilePacket.Attachment, null);

                protocol.WritePacket(header, Packet_Name, UTF8Encoding.UTF8.GetBytes(Name));
                protocol.WritePacket(header, Packet_Size, CompactNum.GetBytes(Size));

                return protocol.WriteFinish();
            }
        }

        public static ProfileAttachment Decode(G2Header root)
        {
            ProfileAttachment file = new ProfileAttachment();
            G2Header child = new G2Header(root.Data);

            while (G2Protocol.ReadNextChild(root, child) == G2ReadResult.PACKET_GOOD)
            {
                if (!G2Protocol.ReadPayload(child))
                    continue;

                switch (child.Name)
                {
                    case Packet_Name:
                        file.Name = UTF8Encoding.UTF8.GetString(child.Data, child.PayloadPos, child.PayloadSize);
                        break;

                    case Packet_Size:
                        file.Size = CompactNum.ToInt64(child.Data, child.PayloadPos, child.PayloadSize);
                        break;
                }
            }

            return file;
        }
    }


    public enum ProfileFieldType : byte {Text, File};

    public class ProfileField : G2Packet
    {
        const byte Packet_Type = 0x10;
        const byte Packet_Name = 0x20;
        const byte Packet_Value = 0x30;


        public ProfileFieldType FieldType;
        public string Name;
        public byte[] Value;


        public override byte[] Encode(G2Protocol protocol)
        {
            lock (protocol.WriteSection)
            {
                G2Frame header = protocol.WritePacket(null, ProfilePacket.Field, null);

                protocol.WritePacket(header, Packet_Type, BitConverter.GetBytes((byte)FieldType));
                protocol.WritePacket(header, Packet_Name, UTF8Encoding.UTF8.GetBytes(Name));
                protocol.WritePacket(header, Packet_Value, Value);

                return protocol.WriteFinish();
            }
        }

        public static ProfileField Decode(G2Header root)
        {
            ProfileField field = new ProfileField();
            G2Header child = new G2Header(root.Data);

            while (G2Protocol.ReadNextChild(root, child) == G2ReadResult.PACKET_GOOD)
            {
                if (!G2Protocol.ReadPayload(child))
                    continue;

                switch (child.Name)
                {
                    case Packet_Type:
                        field.FieldType = (ProfileFieldType)child.Data[child.PayloadPos];
                        break;

                    case Packet_Name:
                        field.Name = UTF8Encoding.UTF8.GetString(child.Data, child.PayloadPos, child.PayloadSize);
                        break;

                    case Packet_Value:
                        field.Value = Utilities.ExtractBytes(child.Data, child.PayloadPos, child.PayloadSize);
                        break;
                }
            }

            return field;
        }
    }
}
