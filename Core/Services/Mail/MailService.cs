using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using DeOps.Implementation;
using DeOps.Implementation.Dht;
using DeOps.Implementation.Transport;
using DeOps.Implementation.Protocol;
using DeOps.Implementation.Protocol.Net;

using DeOps.Services.Assist;
using DeOps.Services.Transfer;
using DeOps.Services.Trust;

/* files
 *      mail folder
 *          outbound file (mail headers)
 *          inbound file (mail headers)
 *          inbound/outbound mail files
 *      cache folder
 *          cache file
 *              mail headers
 *              acks
 *              pending headers (including local pending header)
 *          mail/pending files (including local pending file)
 * */

/* entry removal
 *      mail map
 *          ID not in source's pending mail list
 *          ID in target's pending ack list
 *      ack map
 *          ID not in target's pending mail list
 *          ID not in source's pending ack list
 *      pending mail
 *          Ack received from target
 *          ID in targets pending ack list
 *      pending ack
 *          ID not in targets pending mail list
 * */

/* re-publish (make sure pending lists of targets updated first)
 *      mail
 *          search for ID returns nothing
 *      ack 
 *          search for ID returns nothing
 * */


namespace DeOps.Services.Mail
{
    /* when mail is sent, and entry is put in our local pending header that is published locally
     * the mail is sent to the target dht id and cached there
     * when received, an ack is created and sent back, the ack is also stored in the receivers pending file
     * 
     * only cache mail if target is verified in our hierarchy
     * only cache ack if target is verified in our hierarchy
     * */


    enum MailBoxType { Inbox, Outbox }

    public delegate void MailUpdateHandler(LocalMail message);


    public class MailService : OpService
    {
        public string Name { get { return "Mail"; } }
        public uint ServiceID { get { return (uint)ServiceIDs.Mail; } }

        const uint DataTypeLocal = 0x01;
        const uint DataTypePending = 0x02;
        const uint DataTypeMail = 0x03;
        const uint DataTypeAck = 0x04;

        public OpCore Core;
        G2Protocol Protocol;
        public DhtNetwork Network;
        public DhtStore Store;

        public string MailPath;
        public string CachePath;
        byte[] LocalFileKey;
        RijndaelManaged MailIDKey;

        // cached mail/acks for network
        public Dictionary<ulong, List<CachedMail>> MailMap = new Dictionary<ulong, List<CachedMail>>();
        public Dictionary<ulong, List<CachedAck>>  AckMap  = new Dictionary<ulong, List<CachedAck>>();
        public Dictionary<ulong, CachedPending> PendingMap = new Dictionary<ulong, CachedPending>();


        public bool SaveMailbox;
        public ThreadedSortedList<DateTime, LocalMail> LocalMailbox;

        // local pending mails and acks
        public Dictionary<ulong, List<ulong>>  PendingMail = new Dictionary<ulong, List<ulong>>();
        public Dictionary<ulong, List<byte[]>> PendingAcks = new Dictionary<ulong, List<byte[]>>();

        bool RunSaveHeaders;
        Dictionary<ulong, List<MailIdent>> DownloadMailLater = new Dictionary<ulong,List<MailIdent>>();
        Dictionary<ulong, List<MailIdent>> DownloadAcksLater = new Dictionary<ulong, List<MailIdent>>();

        public MailUpdateHandler MailUpdate;

        int PruneSize = 64;

        VersionedCache PendingCache;

        public delegate void ShowComposeHandler(ulong userID, LocalMail message, string title, string body);
        public ShowComposeHandler ShowCompose;


        public MailService(OpCore core)
        {
            Core = core;
            Network = core.Network;
            Protocol = Network.Protocol;
            Store = Network.Store;

            Core.SecondTimerEvent += Core_SecondTimer;
            Core.MinuteTimerEvent += Core_MinuteTimer;

            Network.CoreStatusChange += new StatusChange(Network_StatusChange);

            Store.StoreEvent[ServiceID, DataTypeMail] += new StoreHandler(Store_LocalMail);
            Store.StoreEvent[ServiceID, DataTypeAck] += new StoreHandler(Store_LocalAck);

            Network.Searches.SearchEvent[ServiceID, DataTypeMail] += new SearchRequestHandler(Search_LocalMail);
            Network.Searches.SearchEvent[ServiceID, DataTypeAck] += new SearchRequestHandler(Search_LocalAck);

            Store.ReplicateEvent[ServiceID, DataTypeMail] += new ReplicateHandler(Store_ReplicateMail);
            Store.ReplicateEvent[ServiceID, DataTypeAck] += new ReplicateHandler(Store_ReplicateAck);

            Store.PatchEvent[ServiceID, DataTypeMail] += new PatchHandler(Store_PatchMail);
            Store.PatchEvent[ServiceID, DataTypeAck] += new PatchHandler(Store_PatchAck);

            Core.Transfers.FileSearch[ServiceID, DataTypeMail] += new FileSearchHandler(Transfers_MailSearch);
            Core.Transfers.FileRequest[ServiceID, DataTypeMail] += new FileRequestHandler(Transfers_MailRequest);

            if (Core.Sim != null)
                PruneSize = 16;

            LocalFileKey = Core.User.Settings.FileKey;

            MailIDKey = new RijndaelManaged();
            MailIDKey.Key = LocalFileKey;
            MailIDKey.IV = new byte[MailIDKey.IV.Length];
            MailIDKey.Padding = PaddingMode.None;

            MailPath = Core.User.RootPath + Path.DirectorySeparatorChar + "Data" + Path.DirectorySeparatorChar + ServiceID.ToString();
            CachePath = MailPath + Path.DirectorySeparatorChar + "1";

            Directory.CreateDirectory(MailPath);
            Directory.CreateDirectory(CachePath);

            PendingCache = new VersionedCache(Network, ServiceID, DataTypePending, false);

            PendingCache.FileAquired += new FileAquiredHandler(PendingCache_FileAquired);
            PendingCache.FileRemoved += new FileRemovedHandler(PendingCache_FileRemoved);
            PendingCache.Load();

            LoadHeaders();
        }

        public void Dispose()
        {
            Core.SecondTimerEvent -= Core_SecondTimer;
            Core.MinuteTimerEvent -= Core_MinuteTimer;

            Network.CoreStatusChange -= new StatusChange(Network_StatusChange);

            Store.StoreEvent[ServiceID, DataTypeMail] -= new StoreHandler(Store_LocalMail);
            Store.StoreEvent[ServiceID, DataTypeAck] -= new StoreHandler(Store_LocalAck);

            Network.Searches.SearchEvent[ServiceID, DataTypeMail] -= new SearchRequestHandler(Search_LocalMail);
            Network.Searches.SearchEvent[ServiceID, DataTypeAck] -= new SearchRequestHandler(Search_LocalAck);

            Store.ReplicateEvent[ServiceID, DataTypeMail] -= new ReplicateHandler(Store_ReplicateMail);
            Store.ReplicateEvent[ServiceID, DataTypeAck] -= new ReplicateHandler(Store_ReplicateAck);

            Store.PatchEvent[ServiceID, DataTypeMail] -= new PatchHandler(Store_PatchMail);
            Store.PatchEvent[ServiceID, DataTypeAck] -= new PatchHandler(Store_PatchAck);

            PendingCache.FileAquired -= new FileAquiredHandler(PendingCache_FileAquired);
            PendingCache.FileRemoved -= new FileRemovedHandler(PendingCache_FileRemoved);
            PendingCache.Dispose();

            Core.Transfers.FileSearch[ServiceID, DataTypeMail] -= new FileSearchHandler(Transfers_MailSearch);
            Core.Transfers.FileRequest[ServiceID, DataTypeMail] -= new FileRequestHandler(Transfers_MailRequest);

        }

        void Core_SecondTimer()
        {
            //crit periodically do search for unacked list so maybe mail/acks can be purged

            // unload outbound / inbound data if no interfaces connected


            if (RunSaveHeaders)
                SaveHeaders();

            if (SaveMailbox && LocalMailbox != null)
                SaveLocalHeaders();

            // clean download later map
            if (!Network.Established)
            {
                PruneMap(DownloadMailLater);
                PruneMap(DownloadAcksLater);
            }
        }

        void Core_MinuteTimer()
        {
            // prune mail items
            foreach (List<CachedMail> list in MailMap.Values)
                if (list.Count > PruneSize) // no other way to ident, cant remove by age
                    list.RemoveRange(0, list.Count - PruneSize);

            // prune mail 
            if (MailMap.Count > PruneSize)
                foreach (ulong id in (from target in MailMap.Keys 
                                      where !Core.KeepData.SafeContainsKey(target)
                                      orderby target ^ Core.UserID descending
                                      select target).Take(MailMap.Count - PruneSize).ToArray())
                {
                    foreach (CachedMail item in MailMap[id])
                        if (item.Header != null)
                            try { File.Delete(GetCachePath(item.Header)); }
                            catch { }

                    MailMap.Remove(id);
                    RunSaveHeaders = true;
                }


            // prune ack items
            foreach (List<CachedAck> list in AckMap.Values)
                if (list.Count > PruneSize) // no other way to ident, cant remove by age
                    list.RemoveRange(0, list.Count - PruneSize);


            // prune acks 
            if (AckMap.Count > PruneSize)
                foreach (ulong id in (from target in AckMap.Keys
                                      where !Core.KeepData.SafeContainsKey(target)
                                      orderby target ^ Core.UserID descending
                                      select target).Take(AckMap.Count - PruneSize).ToArray())
                {
                    AckMap.Remove(id);
                    RunSaveHeaders = true;
                }
        }

        public void SimTest()
        {
            if (Core.InvokeRequired)
            {
                Core.RunInCoreAsync(delegate() { SimTest(); });
                return;
            }

            int choice = Core.RndGen.Next(100);

            int thread = 0;

            // mail
            if (choice < 25)
            {

            }

            // reply
            else if (LocalMailbox != null)
            {
                LocalMailbox.LockReading(delegate()
                {
                    int index = Core.RndGen.Next(LocalMailbox.Count);

                    int i = 0;
                    foreach (LocalMail mail in LocalMailbox.Values)
                    {
                        if (i == index)
                        {
                            thread = mail.Header.ThreadID;
                            break;
                        }

                        i++;
                    }
                });
            }

            else
                return;

            OpTrust target = Core.Trust.GetRandomTrust();

            if (target == null)
                return;

            string subject = Core.TextGen.GenerateSentences(1)[0];
            string body = "";

            body = Core.TextGen.GenerateParagraphs(1)[0];
            //foreach (string paragraph in Core.TextGen.GenerateParagraphs(Core.RndGen.Next(1,3)))
            //   body += paragraph + "\r\n\r\n";

            SendMail(new List<ulong>() { target.UserID }, new List<AttachedFile>(), subject, body, TextFormat.Plain, "", thread);
        }

        public void SimCleanup()
        {
        }

        void PendingCache_FileRemoved(OpVersionedFile file)
        {
            CachedPending pending = FindPending(file.UserID);

            if (pending != null)
                PendingMap.Remove(file.UserID);
        }

        private void PruneMap(Dictionary<ulong, List<MailIdent>> map)
        {
            if (map.Count < PruneSize)
                return;

            List<ulong> removeIDs = new List<ulong>();

            while (map.Count > 0 && map.Count > PruneSize)
            {
                ulong furthest = Core.UserID;

                // get furthest id
                foreach (ulong id in map.Keys)
                    if ((id ^ Core.UserID) > (furthest ^ Core.UserID))
                        furthest = id;

                // remove one 
                map.Remove(furthest);
            }  
        }

        void Network_StatusChange()
        {
            if (Network.Established)
            {
                // re-publish acks
                foreach (ulong key in AckMap.Keys)
                    foreach (CachedAck ack in AckMap[key])
                        if (ack.Unique && Network.Routing.InCacheArea(key))
                            Store.PublishNetwork(key, ServiceID, DataTypeAck, ack.SignedAck);

                // only download those objects in our local area
                foreach (ulong key in DownloadAcksLater.Keys)
                    if (Network.Routing.InCacheArea(key))
                        foreach (MailIdent ident in DownloadAcksLater[key])
                            StartAckSearch(key, ident.Encode());

                DownloadAcksLater.Clear();

                // re-publish mail
                foreach (ulong key in MailMap.Keys)
                    foreach (CachedMail mail in MailMap[key])
                        if (mail.Unique && Network.Routing.InCacheArea(key))
                            Store.PublishNetwork(key, ServiceID, DataTypeMail, mail.SignedHeader);


                // only download those objects in our local area
                foreach (ulong key in DownloadMailLater.Keys)
                    if (Network.Routing.InCacheArea(key))
                        foreach (MailIdent ident in DownloadMailLater[key])
                            StartMailSearch(key, ident.Encode());

                DownloadMailLater.Clear();
            }

            // disconnected, reset cache to unique
            else if (!Network.Responsive)
            {
                foreach (ulong key in AckMap.Keys)
                    foreach (CachedAck ack in AckMap[key])
                        ack.Unique = true;


                foreach (ulong key in MailMap.Keys)
                    foreach (CachedMail mail in MailMap[key])
                        mail.Unique = true;
            }
        }

        void SaveHeaders()
        {
            if (Core.InvokeRequired)
                Debug.Assert(false);

            RunSaveHeaders = false;

            try
            {
                string tempPath = Core.GetTempPath();
                using (IVCryptoStream stream = IVCryptoStream.Save(tempPath, LocalFileKey))
                {
                    // mail headers
                    foreach (List<CachedMail> list in MailMap.Values)
                        foreach (CachedMail mail in list)
                            stream.Write(mail.SignedHeader, 0, mail.SignedHeader.Length);

                    // acks
                    foreach (List<CachedAck> list in AckMap.Values)
                        foreach (CachedAck ack in list)
                            stream.Write(ack.SignedAck, 0, ack.SignedAck.Length);

                    stream.FlushFinalBlock();
                }

                string finalPath = CachePath + Path.DirectorySeparatorChar + Utilities.CryptFilename(Core, "headers");
                File.Delete(finalPath);
                File.Move(tempPath, finalPath);
            }
            catch (Exception ex)
            {
                Core.Network.UpdateLog("Mail", "Error saving headers " + ex.Message);
            }
        }

        void LoadHeaders()
        {
            try
            {
                string path = CachePath + Path.DirectorySeparatorChar + Utilities.CryptFilename(Core, "headers");

                if (!File.Exists(path))
                    return;

                using (IVCryptoStream crypto = IVCryptoStream.Load(path, LocalFileKey))
                {
                    PacketStream stream = new PacketStream(crypto, Network.Protocol, FileAccess.Read);

                    G2Header root = null;

                    while (stream.ReadPacket(ref root))
                        if (root.Name == DataPacket.SignedData)
                        {
                            SignedData signed = SignedData.Decode(root);
                            G2Header embedded = new G2Header(signed.Data);

                            // figure out data contained
                            if (G2Protocol.ReadPacket(embedded))
                            {
                                if (embedded.Name == MailPacket.MailHeader)
                                    Process_MailHeader(null, signed, MailHeader.Decode(embedded));

                                else if (embedded.Name == MailPacket.Ack)
                                    Process_MailAck(null, signed, MailAck.Decode(embedded), true);
                            }
                        }
                }
            }
            catch (Exception ex)
            {
                Core.Network.UpdateLog("Mail", "Error loading headers " + ex.Message);
            }
        }

        public void SaveLocalHeaders()
        {           
            if (Core.InvokeRequired)
                Debug.Assert(false);

            SaveMailbox = false;
 
            try
            {
                string tempPath = Core.GetTempPath();
                using (IVCryptoStream stream = IVCryptoStream.Save(tempPath, LocalFileKey))
                {
                    LocalMailbox.LockReading(delegate()
                    {
                        foreach (LocalMail local in LocalMailbox.Values)
                        {
                            byte[] encoded = local.Header.Encode(Network.Protocol, true);
                            stream.Write(encoded, 0, encoded.Length);
                        }
                    });

                    stream.FlushFinalBlock();
                }


                string finalPath = MailPath + Path.DirectorySeparatorChar + Utilities.CryptFilename(Core, "mailbox");
                File.Delete(finalPath);
                File.Move(tempPath, finalPath);
            }
            catch (Exception ex)
            {
                Core.Network.UpdateLog("Mail", "Error saving mailbox " + ex.Message);
            }
        }

        public void LoadLocalHeaders()
        {
            List<MailHeader> headers = new List<MailHeader>();

 
            string path = MailPath + Path.DirectorySeparatorChar + Utilities.CryptFilename(Core, "mailbox");

            if (File.Exists(path))
                try
                {
                    using (IVCryptoStream crypto = IVCryptoStream.Load(path, LocalFileKey))
                    {
                        PacketStream stream = new PacketStream(crypto, Network.Protocol, FileAccess.Read);

                        G2Header root = null;

                        while (stream.ReadPacket(ref root))
                            if (root.Name == MailPacket.MailHeader)
                            {
                                MailHeader header = MailHeader.Decode(root);

                                if (header == null)
                                    continue;

                                Core.IndexKey(header.SourceID, ref header.Source);

                                if (header.Target != null)
                                    Core.IndexKey(header.TargetID, ref header.Target);

                                headers.Add(header);
                            }
                    }
                }
                catch (Exception ex)
                {
                    Core.Network.UpdateLog("Mail", "Error loading mailbox " + ex.Message);
                }

            // load mail files that headers point to
            LocalMailbox = new ThreadedSortedList<DateTime, LocalMail>();

            foreach (MailHeader header in headers)
            {
                LocalMail local = LoadLocalMail(header);

                if (local != null)
                    AddToLocalMail(local.Info.Date, local);
            }
        }

        private LocalMail LoadLocalMail(MailHeader header)
        {
            LocalMail local = new LocalMail();
            local.Header = header;
            local.From = header.SourceID;

            try
            {
                string path = GetLocalPath(header);

                if (!File.Exists(path))
                    return null;

                using (TaggedStream file = new TaggedStream(path, Network.Protocol))
                using (IVCryptoStream crypto = IVCryptoStream.Load(file, header.LocalKey))
                {
                    PacketStream stream = new PacketStream(crypto, Network.Protocol, FileAccess.Read);

                    G2Header root = null;

                    while (stream.ReadPacket(ref root))
                    {
                        if (root.Name == MailPacket.MailInfo)
                            local.Info = MailInfo.Decode(root);

                        else if (root.Name == MailPacket.MailDest)
                        {
                            MailDestination dest = MailDestination.Decode(root);
                            Core.IndexKey(dest.KeyID, ref dest.Key);

                            if (dest.CC)
                                local.CC.Add(dest.KeyID);
                            else
                                local.To.Add(dest.KeyID);
                        }

                        else if (root.Name == MailPacket.MailFile)
                            local.Attached.Add(MailFile.Decode(root));
                    }
                }
            }
            catch (Exception ex)
            {
                Core.Network.UpdateLog("Mail", "Error loading local mail " + ex.Message);
            }

            if (local.Info != null)
                return local;

            return null;
        }

        public void SendMail(List<ulong> to, List<AttachedFile> files, string subject, string body, TextFormat format, string quip, int threadID)
        {
            if (Core.InvokeRequired)
            {
                Core.RunInCoreBlocked(delegate() { SendMail(to, files, subject, body, format, quip, threadID); });
                return;
            }

            // exception handled by compose mail interface
            MailHeader header = new MailHeader();
            header.Source = Core.User.Settings.KeyPublic;
            header.SourceID = Core.UserID;
            header.LocalKey = Utilities.GenerateKey(Core.StrongRndGen, 256);
            header.Read = true;

            // setup temp file
            string tempPath = Core.GetTempPath();
            using (IVCryptoStream stream = IVCryptoStream.Save(tempPath, header.LocalKey))
            {
                long written = 0;

                // build mail file
                written += Protocol.WriteToFile(new MailInfo(subject, format, quip, Core.TimeNow.ToUniversalTime(), files.Count > 0), stream);

                foreach (ulong id in to)
                    written += Protocol.WriteToFile(new MailDestination(Core.KeyMap[id], false), stream);

                byte[] bodyBytes = UTF8Encoding.UTF8.GetBytes(body);
                written += Protocol.WriteToFile(new MailFile("body", bodyBytes.Length), stream);

                foreach (AttachedFile attached in files)
                    written += Protocol.WriteToFile(new MailFile(attached.Name, attached.Size), stream);

                stream.WriteByte(0); // end packets
                header.FileStart = written + 1;

                // write files
                stream.Write(bodyBytes, 0, bodyBytes.Length);

                if (files != null)
                {
                    int buffSize = 4096;
                    byte[] buffer = new byte[buffSize];

                    foreach (AttachedFile attached in files)
                        using (FileStream embed = File.OpenRead(attached.FilePath))
                        {

                            int read = buffSize;
                            while (read == buffSize)
                            {
                                read = embed.Read(buffer, 0, buffSize);
                                stream.Write(buffer, 0, read);
                            }
                        }
                }

                stream.FlushFinalBlock();
            }

            // finish building header
            Utilities.HashTagFile(tempPath, Network.Protocol, ref header.FileHash, ref header.FileSize);


            // move file, overwrite if need be, local id used so filename is the same for all targets
            string finalPath = MailPath + Path.DirectorySeparatorChar + Utilities.CryptFilename(Core, Core.UserID, header.FileHash);
            File.Move(tempPath, finalPath);

            // write header to outbound file
            if (LocalMailbox == null)
                LoadLocalHeaders();

            LocalMail message = LoadLocalMail(header);

            if (message != null)
            {
                AddToLocalMail(message.Info.Date, message);
                SaveMailbox = true;

                if (MailUpdate != null)
                    Core.RunInGuiThread(MailUpdate, message);
            }

            // write headers to outbound cache file
            List<ulong> targets = new List<ulong>();
            targets.AddRange(to);

            // add targets as unacked
            ulong hashID = BitConverter.ToUInt64(header.FileHash, 0);

            List<KeyValuePair<ulong, byte[]>> publishTargets = new List<KeyValuePair<ulong, byte[]>>();
            foreach (ulong id in targets)
            {
                if (!PendingMail.ContainsKey(hashID))
                    PendingMail[hashID] = new List<ulong>();

                PendingMail[hashID].Add(id);
            }

            SavePending();

            CachedPending local = FindPending(Core.UserID);
            header.ThreadID = (threadID != 0) ? threadID : Core.RndGen.Next();
            header.SourceVersion = (local != null) ? local.Header.Version : 0;

            // publish to targets
            foreach (ulong id in targets)
            {
                header.Target = Core.KeyMap[id];
                header.TargetID = id;
                // encode file's encryption key with target's public key
                header.FileKey = EncodeFileKey(Utilities.KeytoRsa(Core.KeyMap[id]), header.LocalKey, header.FileStart);
                header.MailID  = GetMailID(hashID, id);

                if (PendingMap.ContainsKey(id))
                    header.TargetVersion = PendingMap[id].Header.Version;

                byte[] signed = SignedData.Encode(Network.Protocol, Core.User.Settings.KeyPair, header.Encode(Network.Protocol, false) );

                if(Network.Established)
                    Core.Network.Store.PublishNetwork(id, ServiceID, DataTypeMail, signed);

                Store_LocalMail(new DataReq(null, id, ServiceID, DataTypeMail, signed)); // cant direct process_header, because header var is being modified
            }

            SaveHeaders();
        }

        // key to decrypt the email file is encoded with the receiver's public key
        byte[] EncodeFileKey(RSACryptoServiceProvider rsa, byte[] crypt, long fileStart)
        {
            byte[] buffer = new byte[crypt.Length + 8];

            crypt.CopyTo(buffer, 0);
            BitConverter.GetBytes(fileStart).CopyTo(buffer, crypt.Length);

            return rsa.Encrypt(buffer, false);
        }

        void DecodeFileKey(byte[] encoded, ref byte[] fileKey, ref long fileStart)
        {
            byte[] decoded = Core.User.Settings.KeyPair.Decrypt(encoded, false);

            if (decoded.Length < (256 / 8) + 8) // 256 bit encryption is 32 bytes plus 8 for fileStart data
                return;

            fileKey = Utilities.ExtractBytes(decoded, 0, 256/8);

            fileStart = BitConverter.ToInt64(decoded, fileKey.Length);
        }

        public byte[] GetMailID(ulong hashID, ulong userID)
        {
            // random bytes in MailInfo ensure that the hash ID is different for all messages

            byte[] buffer = new byte[16];

            BitConverter.GetBytes(hashID).CopyTo(buffer, 0);
            BitConverter.GetBytes(userID).CopyTo(buffer, 8);

            ICryptoTransform transform = MailIDKey.CreateEncryptor();
            buffer = transform.TransformFinalBlock(buffer, 0, buffer.Length);

            return buffer;
        }

        private void StartMailSearch(ulong key, byte[] parameters)
        {
            DhtSearch search = Core.Network.Searches.Start(key, "Mail", ServiceID, DataTypeMail, parameters, Search_MailFound);

            if (search != null)
                search.TargetResults = 2;
        }

        void Search_MailFound(DhtSearch search, DhtAddress source, byte[] data)
        {
            Store_LocalMail(new DataReq(source, search.TargetID, ServiceID, 0, data));
        }

        private void StartAckSearch(ulong key, byte[] parameters)
        {
            DhtSearch search = Core.Network.Searches.Start(key, "Mail", ServiceID, DataTypeAck, parameters, Search_AckFound);

            if (search != null)
                search.TargetResults = 2;
        }

        void Search_AckFound(DhtSearch search, DhtAddress source, byte[] data)
        {
            Store_LocalAck(new DataReq(source, search.TargetID, ServiceID, 0, data));
        }

        void Search_LocalMail(ulong key, byte[] parameters, List<byte[]> results)
        {
            MailIdent ident = MailIdent.Decode(parameters, 0);

            CachedMail cached = FindMail(ident);

            if (cached != null)
                results.Add(cached.SignedHeader);
        }

        void Search_LocalAck(ulong key, byte[] parameters, List<byte[]> results)
        {
            MailIdent ident = MailIdent.Decode(parameters, 0);

            CachedAck cached = FindAck(ident);

            if (cached != null)
                results.Add(cached.SignedAck);
        }

        bool Transfers_MailSearch(ulong key, FileDetails details)
        {
            if (MailMap.ContainsKey(key))
            {
                List<CachedMail> list = MailMap[key];

                foreach (CachedMail mail in list)
                    if (details.Size == mail.Header.FileSize && Utilities.MemCompare(details.Hash, mail.Header.FileHash))
                        return true;
            }

            return false;
        }

        string Transfers_MailRequest(ulong key, FileDetails details)
        {
            if (MailMap.ContainsKey(key))
            {
                List<CachedMail> list = MailMap[key];

                foreach (CachedMail mail in list)
                    if (details.Size == mail.Header.FileSize && Utilities.MemCompare(details.Hash, mail.Header.FileHash))
                    {
                        string path = GetCachePath(mail.Header);
                        if (File.Exists(path))
                            return path;

                        path = GetLocalPath(mail.Header);
                        if (File.Exists(path))
                            return path;
                    }
            }

            return null;
        }

        void Store_LocalMail(DataReq store)
        {
            // getting published to - search results - patch

            SignedData signed = SignedData.Decode(store.Data);

            if (signed == null)
                return;

            G2Header embedded = new G2Header(signed.Data);

            // figure out data contained
            if (G2Protocol.ReadPacket(embedded))
                if (embedded.Name == MailPacket.MailHeader)
                    Process_MailHeader(store, signed, MailHeader.Decode(embedded));
        }

        void Store_LocalAck(DataReq store)
        {
            // getting published to - search results - patch

            SignedData signed = SignedData.Decode(store.Data);

            if (signed == null)
                return;

            G2Header embedded = new G2Header(signed.Data);

            // figure out data contained
            if (G2Protocol.ReadPacket(embedded))
                if (embedded.Name == MailPacket.Ack)
                    Process_MailAck(store, signed, MailAck.Decode(embedded), false);
        }

        List<byte[]> Store_ReplicateMail(DhtContact contact)
        {
            List<byte[]> patches = new List<byte[]>();

            foreach (List<CachedMail> list in MailMap.Values)
                foreach (CachedMail cached in list)
                    if( Network.Routing.InCacheArea(cached.Header.TargetID))
                        patches.Add(new MailIdent(cached.Header.TargetID, Utilities.ExtractBytes(cached.Header.MailID, 0, 4)).Encode());

            return patches;
        }

        List<byte[]> Store_ReplicateAck(DhtContact contact)
        {
            List<byte[]> patches = new List<byte[]>();

            // acks
            foreach (List<CachedAck> list in AckMap.Values)
                foreach (CachedAck cached in list)
                    if(Network.Routing.InCacheArea(cached.Ack.TargetID))
                        patches.Add( new MailIdent(cached.Ack.TargetID, Utilities.ExtractBytes(cached.Ack.MailID, 0, 4)).Encode());

            return patches;
        }

        void Store_PatchMail(DhtAddress source, byte[] data)
        {
            if (data.Length % MailIdent.SIZE != 0)
                return;

            int offset = 0;
            for (int i = 0; i < data.Length; i += MailIdent.SIZE)
            {
                MailIdent ident = MailIdent.Decode(data, i);
                offset += MailIdent.SIZE;

                if (!Network.Routing.InCacheArea(ident.UserID))
                    continue;

                CachedMail mail = FindMail(ident);

                if (mail != null)
                {
                    mail.Unique = false;
                    continue;
                }

                if (Network.Established)
                    Network.Searches.SendDirectRequest(source, ident.UserID, ServiceID, DataTypeMail, ident.Encode());
                else
                {
                    if (!DownloadMailLater.ContainsKey(ident.UserID))
                        DownloadMailLater[ident.UserID] = new List<MailIdent>();

                    DownloadMailLater[ident.UserID].Add(ident);
                }
            }
        }

        void Store_PatchAck(DhtAddress source, byte[] data)
        {
            if (data.Length % MailIdent.SIZE != 0)
                return;

            int offset = 0;
            for (int i = 0; i < data.Length; i += MailIdent.SIZE)
            {
                MailIdent ident = MailIdent.Decode(data, i);
                offset += MailIdent.SIZE;

                if (!Network.Routing.InCacheArea(ident.UserID))
                    continue;

                CachedAck ack = FindAck(ident);

                if (ack != null)
                {
                    ack.Unique = false;
                    continue;
                }

                if (Network.Established)
                    Network.Searches.SendDirectRequest(source, ident.UserID, ServiceID, DataTypeAck, ident.Encode());
                else
                {
                    if (!DownloadAcksLater.ContainsKey(ident.UserID))
                        DownloadAcksLater[ident.UserID] = new List<MailIdent>();

                    DownloadAcksLater[ident.UserID].Add(ident);
                }
            }
        }

        CachedMail FindMail(MailIdent ident)
        {
            if (MailMap.ContainsKey(ident.UserID))
                foreach (CachedMail cached in MailMap[ident.UserID])
                    if (Utilities.MemCompare(cached.Header.MailID, 0, ident.Data, 0, 4))
                        return cached;

            return null;
        }

        CachedAck FindAck(MailIdent ident)
        {
            if (AckMap.ContainsKey(ident.UserID))
                foreach (CachedAck cached in AckMap[ident.UserID])
                    if (Utilities.MemCompare(cached.Ack.MailID, 0, ident.Data, 0, 4))
                        return cached;

            return null;
        }

        private CachedPending FindPending(ulong id)
        {
            if (PendingMap.ContainsKey(id))
                return PendingMap[id];

            return null;
        }

        private void Process_MailHeader(DataReq data, SignedData signed, MailHeader header)
        {
            Core.IndexKey(header.SourceID, ref header.Source);
            Core.IndexKey(header.TargetID, ref header.Target);

            // check if already cached
            if (MailMap.ContainsKey(header.TargetID))
                foreach (CachedMail cached in MailMap[header.TargetID])
                    if (Utilities.MemCompare(header.MailID, cached.Header.MailID))
                        return;

            bool cache = false;

            // source's pending file
            if (PendingMap.ContainsKey(header.SourceID))
            {
                CachedPending pending = PendingMap[header.SourceID];

                if (header.SourceVersion > pending.Header.Version)
                {
                    PendingCache.Research(header.SourceID);
                    cache = true;
                }
                else
                {
                    
                    if (IsMailPending(pending, header.MailID))
                        cache = true;
                    else
                        return; //ID not in source's pending mail list
                }
            }
            else
            {
                PendingCache.Research(header.SourceID);
                cache = true;
            }

            // check target's pending file
            if (PendingMap.ContainsKey(header.TargetID))
            {
                CachedPending pending = PendingMap[header.TargetID];

                if (header.TargetVersion > pending.Header.Version)
                {
                    PendingCache.Research(header.TargetID);
                    cache = true;
                }
                else
                {
                    if (IsAckPending(pending, header.MailID))
                        return;  //ID in target's pending ack list
                    else
                        cache = true;
                }
            }
            else
            {
                PendingCache.Research(header.TargetID);
                cache = true;
            }

            if(cache)
                CacheMail(signed, header);
        }

        private void Download_Mail(SignedData signed, MailHeader header)
        {
            if (!Utilities.CheckSignedData(header.Source, signed.Data, signed.Signature))
                return;

            FileDetails details = new FileDetails(ServiceID, DataTypeMail, header.FileHash, header.FileSize, null);

            Core.Transfers.StartDownload(header.TargetID, details, GetCachePath(header), new EndDownloadHandler(EndDownload_Mail), new object[] { signed, header });
        }

        private void EndDownload_Mail(object[] args)
        {
            SignedData signedHeader = (SignedData)args[0];
            MailHeader header = (MailHeader)args[1];

            CacheMail(signedHeader, header);
        }

        private void CacheMail(SignedData signed, MailHeader header)
        {
            if (Core.InvokeRequired)
                Debug.Assert(false);

            try
            {
                bool exists = false;

                // try cache path
                string path = GetCachePath(header);
                if (File.Exists(path))
                    exists = true;

                // try local path
                if (!exists)
                {
                    path = GetLocalPath(header);
                    if (File.Exists(path))
                        exists = true;
                }

                if(!exists)
                {
                    Download_Mail(signed, header);
                    return;
                }

                if(header.TargetID == Core.UserID)
                {
                    ReceiveMail(path, signed, header);
                    return;
                }

                if(!MailMap.ContainsKey(header.TargetID))
                    MailMap[header.TargetID] = new List<CachedMail>();

                CachedMail mail = new CachedMail() ;

                mail.Header = header;
                mail.SignedHeader = signed.Encode(Network.Protocol);
                mail.Unique = !Network.Established;

                MailMap[header.TargetID].Add(mail);

                RunSaveHeaders = true;
            }
            catch (Exception ex)
            {
                Core.Network.UpdateLog("Mail", "Error loading mail " + ex.Message);
            }
        }

        private void ReceiveMail(string path, SignedData signed, MailHeader header)
        {
            header.Received = Core.TimeNow.ToUniversalTime();

            // move file
            string localPath = GetLocalPath(header);
            string cachePath = GetCachePath(header);
           
            if (File.Exists(localPath))
                return;

            File.Copy(cachePath, localPath, true);

            // add to inbound list
            if (LocalMailbox == null)
                LoadLocalHeaders();

            DecodeFileKey(header.FileKey, ref header.LocalKey, ref header.FileStart);

            LocalMail message = LoadLocalMail(header);

            if (message != null)
            {
                AddToLocalMail(message.Info.Date, message);

                SaveMailbox = true;

                if (MailUpdate != null)
                    Core.RunInGuiThread(MailUpdate, message);
            }

            // publish ack
            MailAck ack  = new MailAck();
            ack.MailID   = header.MailID;
            ack.Source   = Core.User.Settings.KeyPublic;
            ack.SourceID = Core.UserID;
            ack.Target   = header.Source;
            ack.TargetID = header.SourceID; 
            ack.TargetVersion = header.SourceVersion;
            
            // update pending
            if (!PendingAcks.ContainsKey(ack.TargetID))
                PendingAcks[ack.TargetID] = new List<byte[]>();
            PendingAcks[ack.TargetID].Add(ack.MailID);

            SavePending(); // also publishes
            
            CachedPending local = FindPending(Core.UserID);
            ack.SourceVersion = (local != null) ? local.Header.Version : 0;

            // send 
            byte[] signedAck = SignedData.Encode(Network.Protocol, Core.User.Settings.KeyPair, ack.Encode(Network.Protocol));
            
            if(Network.Established)
                Core.Network.Store.PublishNetwork(header.SourceID, ServiceID, DataTypeAck, signedAck);

            Store_LocalAck(new DataReq(null, header.SourceID, ServiceID, DataTypeAck, signedAck)); // cant direct process_header, because header var is being modified
            RunSaveHeaders = true;
            
            Core.MakeNews(ServiceIDs.Mail, "Mail Received from " + Core.GetName(message.From), message.From, 0, false);
         
        }

        private void AddToLocalMail(DateTime time, LocalMail message)
        {
            // sorted list doesnt support dupes, mod the ticks a little so it adds
            LocalMailbox.LockWriting(delegate()
            {
                while (LocalMailbox.ContainsKey(time))
                    time = time.AddTicks(1);

                LocalMailbox.Add(time, message);
            });
            
        }

        private bool IsMailPending(CachedPending pending, byte[] mailID)
        {
            try
            {
                string path = PendingCache.GetFilePath(pending.Header);

                using (TaggedStream file = new TaggedStream(path, Network.Protocol))
                using (IVCryptoStream crypto = IVCryptoStream.Load(file, pending.Header.FileKey))
                {
                    byte[] divider = new byte[16];
                    byte[] buffer = new byte[4096];

                    int read = buffer.Length;
                    while (read == buffer.Length)
                    {
                        read = crypto.Read(buffer, 0, buffer.Length);

                        if (read % 16 != 0)
                            return false;

                        for (int i = 0; i < read; i += 16)
                        {
                            if (Utilities.MemCompare(divider, 0, buffer, i, 16))
                                return false;

                            if (Utilities.MemCompare(mailID, 0, buffer, i, 16))
                                return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Core.Network.UpdateLog("Mail", "Error checking pending " + ex.Message);
            }

            return false;
        }

        private bool IsAckPending(CachedPending pending, byte[] mailID)
        {
            try
            {
                string path = PendingCache.GetFilePath(pending.Header);

                using (TaggedStream file = new TaggedStream(path, Network.Protocol))
                using (IVCryptoStream crypto = IVCryptoStream.Load(file, pending.Header.FileKey))
                {
                    bool dividerReached = false;
                    byte[] divider = new byte[16];
                    byte[] buffer = new byte[4096];

                    int read = buffer.Length;
                    while (read == buffer.Length)
                    {
                        read = crypto.Read(buffer, 0, buffer.Length);

                        if (read % 16 != 0)
                            return false;

                        for (int i = 0; i < read; i += 16)
                        {
                            if (Utilities.MemCompare(divider, 0, buffer, i, 16))
                                dividerReached = true;

                            else if (dividerReached && Utilities.MemCompare(mailID, 0, buffer, i, 16))
                                return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Core.Network.UpdateLog("Mail", "Error checking pending " + ex.Message);
            }

            return false;
        }

        private void Process_MailAck(DataReq data, SignedData signed, MailAck ack, bool verified)
        {
            Core.IndexKey(ack.SourceID, ref ack.Source);
            Core.IndexKey(ack.TargetID, ref ack.Target);

            if (!verified)
                if (!Utilities.CheckSignedData(ack.Source, signed.Data, signed.Signature))
                    return;

            // check if local
            if(ack.TargetID == Core.UserID)
            {
                ReceiveAck(ack);
                return;
            }

            // check if already cached
            if (AckMap.ContainsKey(ack.TargetID))
                foreach (CachedAck cached in AckMap[ack.TargetID])
                    if (Utilities.MemCompare(ack.MailID, cached.Ack.MailID))
                        return;

            bool add = true;

            // check target pending file
            if (PendingMap.ContainsKey(ack.TargetID))
            {
                CachedPending pending = PendingMap[ack.TargetID];

                // if our version of the pending file is out of date
                if (ack.TargetVersion > pending.Header.Version)
                    PendingCache.Research(ack.TargetID);

                // not pending, means its not in file, which means it IS acked
                else if (!IsMailPending(pending, ack.MailID))
                    add = false;
            }
            else
                PendingCache.Research(ack.TargetID);

            // check source pending file, but dont search for it or anything, focus on the target, source is backup
            if (PendingMap.ContainsKey(ack.SourceID))
            {
                CachedPending pending = PendingMap[ack.SourceID];

                if (pending.Header.Version > ack.SourceVersion)
                    if (!IsAckPending(pending, ack.MailID))
                        add = false;
            }

            if(add)
            {
                if (!AckMap.ContainsKey(ack.TargetID))
                    AckMap[ack.TargetID] = new List<CachedAck>();

                AckMap[ack.TargetID].Add(new CachedAck(signed.Encode(Network.Protocol), ack, !Network.Established));
            }
        }

        private void ReceiveAck(MailAck ack)
        {
            ulong hashID, userID;
            DecodeMailID(ack.MailID, out hashID, out userID);

            if (userID != ack.SourceID)
                return;

            if (!PendingMail.ContainsKey(hashID))
                return;

            if (!PendingMail[hashID].Contains(userID))
                return;

            PendingMail[hashID].Remove(userID);

            if (PendingMail[hashID].Count == 0)
                PendingMail.Remove(hashID);

            SavePending(); // also publishes

            // update interface
            if (LocalMailbox != null && MailUpdate != null)
                LocalMailbox.LockReading(delegate()
                {
                    foreach (LocalMail message in LocalMailbox.Values)
                        if (Utilities.MemCompare(message.Header.MailID, ack.MailID))
                        {
                            Core.RunInGuiThread(MailUpdate, message);
                            break;
                        }
                });
        }


        void PendingCache_FileAquired(OpVersionedFile cachedfile)
        {
            try
            {
                CachedPending pending = new CachedPending(cachedfile);

                PendingMap[cachedfile.UserID] = pending;

                List<byte[]> pendingMailIDs = new List<byte[]>();
                List<byte[]> pendingAckIDs = new List<byte[]>();

                byte[] buffer = new byte[4096];

                // load pending file
                using (TaggedStream file = new TaggedStream(PendingCache.GetFilePath(cachedfile.Header), Network.Protocol))
                using (IVCryptoStream crypto = IVCryptoStream.Load(file, cachedfile.Header.FileKey))
                {
                    bool dividerPassed = false;
                    byte[] divider = new byte[16];        
                
                    int read = buffer.Length;
                    while (read == buffer.Length)
                    {
                        read = crypto.Read(buffer, 0, buffer.Length);

                        if (read % 16 != 0)
                            throw new Exception("Bad read pending file");

                        for (int i = 0; i < read; i += 16)
                        {
                            byte[] id = Utilities.ExtractBytes(buffer, i, 16);

                            if (Utilities.MemCompare(id, divider))
                                dividerPassed = true;

                            else if (!dividerPassed)
                                pendingMailIDs.Add(id);

                            else
                                pendingAckIDs.Add(id);
                        }
                    }
                }


                // if the local pending file that we are loading
                if (cachedfile.UserID == Core.UserID)
                {
                    // setup local mail pending
                    PendingMail.Clear();

                    foreach (byte[] id in pendingMailIDs)
                    {
                        ulong hashID, userID;
                        DecodeMailID(id, out hashID, out userID);

                        if (!PendingMail.ContainsKey(hashID))
                            PendingMail[hashID] = new List<ulong>();

                        PendingMail[hashID].Add(userID);
                    }

                    // setup local acks pending
                    PendingAcks.Clear();

                    List<ulong> targets = new List<ulong>();

                    string localpath = MailPath + Path.DirectorySeparatorChar + Utilities.CryptFilename(Core, "acktargets");

                    if (File.Exists(localpath))
                    {
                        using (IVCryptoStream crypto = IVCryptoStream.Load(localpath, LocalFileKey))
                        {
                            int read = buffer.Length;
                            while (read == buffer.Length)
                            {
                                read = crypto.Read(buffer, 0, buffer.Length);

                                if (read % 8 != 0)
                                    throw new Exception("Bad read targets file");

                                for (int i = 0; i < read; i += 8)
                                    targets.Add(BitConverter.ToUInt64(buffer, i));
                            }
                        }

                        //crit Debug.Assert(targets.Count == pendingAckIDs.Count);

                        for (int i = 0; i < targets.Count || i > pendingAckIDs.Count; i++)
                        {
                            if (!PendingAcks.ContainsKey(targets[i]))
                                PendingAcks[targets[i]] = new List<byte[]>();

                            PendingAcks[targets[i]].Add(pendingAckIDs[i]);
                        }
                    }
                }   

                CheckCache(pending, pendingMailIDs, pendingAckIDs);
            }
            catch (Exception ex)
            {
                Core.Network.UpdateLog("Mail", "Error loading pending " + ex.Message);
            }
        }

        private void DecodeMailID(byte[] encrypted, out ulong hashID, out ulong userID)
        {
            ICryptoTransform transform = MailIDKey.CreateDecryptor();
            encrypted = transform.TransformFinalBlock(encrypted, 0, encrypted.Length);

            hashID = BitConverter.ToUInt64(encrypted, 0);
            userID  = BitConverter.ToUInt64(encrypted, 8);
        }

        private void CheckCache(CachedPending pending, List<byte[]> mailIDs, List<byte[]> ackIDs)
        {
/* entry removal
 *      mail map 
 *          ID not in source's pending mail list X
 *          ID in target's pending ack list X
 *      ack map
 *          ID not in target's pending mail list X
 *          ID not in source's pending ack list X
 * 
 *      pending mail (local
 *          Ack received from target
 *          ID in targets pending ack list
 *      pending ack
 *          ID not in targets pending mail list
 * */

            VersionedFileHeader header = pending.Header;

            List<string> removePaths = new List<string>();

            // check mail
            List<CachedMail> removeMails = new List<CachedMail>();    
            List<ulong> removeKeys = new List<ulong>();

            foreach (ulong key in MailMap.Keys)
            {
                List<CachedMail> list = MailMap[key];
                removeMails.Clear();

                foreach (CachedMail mail in list)
                {
                    // ID not in source's pending mail list
                    if (mail.Header.SourceID == header.KeyID &&
                        header.Version >= mail.Header.SourceVersion &&
                        !IDinList(mailIDs, mail.Header.MailID))
                        removeMails.Add(mail);
                    

                    // ID in target's pending ack list
                    if (mail.Header.TargetID == pending.Header.KeyID &&
                        header.Version >= mail.Header.TargetVersion &&
                        IDinList(ackIDs, mail.Header.MailID))
                        removeMails.Add(mail);
                }

                foreach (CachedMail mail in removeMails)
                {
                    removePaths.Add(GetCachePath(mail.Header));
                    list.Remove(mail);
                }

                if (list.Count == 0)
                    removeKeys.Add(key);
            }

            foreach (ulong key in removeKeys)
                MailMap.Remove(key);
        

            List<CachedAck> removeAcks = new List<CachedAck>();
            removeKeys.Clear();
            
            // check acks            
            foreach (ulong key in AckMap.Keys)
            {
                List<CachedAck> list = AckMap[key];
                removeAcks.Clear();

                foreach (CachedAck cached in list)
                {
                    // ID not in target's pending mail list
                    if (cached.Ack.TargetID == header.KeyID &&
                        header.Version >= cached.Ack.TargetVersion &&
                        !IDinList(mailIDs, cached.Ack.MailID))
                        removeAcks.Add(cached);

                    // ID not in source's pending ack list
                    if (cached.Ack.SourceID == header.KeyID &&
                        header.Version >= cached.Ack.SourceVersion &&
                        !IDinList(ackIDs, cached.Ack.MailID))
                        removeAcks.Add(cached);
                }
                foreach (CachedAck ack in removeAcks)
                    list.Remove(ack);

                if (list.Count == 0)
                    removeKeys.Add(key);
            }

            foreach (ulong key in removeKeys)
                AckMap.Remove(key);
        

            // mark pending changed and re-save pending
            bool resavePending = false;

            // pending mail, ID in target's pending ack list
            List<ulong> removeTargets = new List<ulong>();
            removeKeys.Clear();

            foreach (ulong hashID in PendingMail.Keys)
            {
                removeTargets.Clear();

                foreach (ulong target in PendingMail[hashID])
                    if (header.KeyID == target)
                    {
                        byte[] mailID = GetMailID(hashID, target);

                        if (IDinList(ackIDs, mailID))
                        {
                            removeTargets.Add(target); // dont have to remove file cause its local
                            resavePending = true;
                        }
                    }

                foreach (ulong target in removeTargets)
                    PendingMail[hashID].Remove(target);

                if (PendingMail[hashID].Count == 0)
                    removeKeys.Add(hashID);
            }

            foreach (ulong hashID in removeKeys)
                PendingMail.Remove(hashID);

            // pending ack, ID not in targets pending mail list
            foreach (ulong target in PendingAcks.Keys)
                if (target == header.KeyID)
                {
                    List<byte[]> removeIDs = new List<byte[]>();

                    foreach (byte[] mailID in PendingAcks[target])
                        if (!IDinList(mailIDs, mailID))
                        {
                            removeIDs.Add(mailID);
                            resavePending = true;
                        }

                    foreach (byte[] mailID in removeIDs)
                        PendingAcks[target].Remove(mailID);

                    if (PendingAcks[target].Count == 0)
                        PendingAcks.Remove(target);

                    break;
                }
                     

            // remove files
            foreach(string path in removePaths)
            {
                try
                {
                    File.Delete(path);
                }
                catch {}
            }

            if (resavePending)
                SavePending();
        }

        private bool IDinList(List<byte[]> ids, byte[] find)
        {
            foreach (byte[] id in ids)
                if (Utilities.MemCompare(id, find))
                    return true;

            return false;
        }

        private void SavePending()
        {
            // pending file is shared over the network, the first part is pending mail IDs
            // the next part is pending ack IDs, because IDs are encrypted ack side needs to seperately
            // store the target ID of the ack locally, local host is only able to decrypt pending mail IDs

            try
            {
                string tempPath = Core.GetTempPath();
                byte[] key = Utilities.GenerateKey(Core.StrongRndGen, 256);
                using (IVCryptoStream stream = IVCryptoStream.Save(tempPath, key))
                {
                    byte[] buffer = null;

                    // write pending mail
                    foreach (ulong hashID in PendingMail.Keys)
                        foreach (ulong userID in PendingMail[hashID])
                        {
                            buffer = GetMailID(hashID, userID);
                            stream.Write(buffer, 0, buffer.Length);
                        }

                    // divider
                    buffer = new byte[16];
                    stream.Write(buffer, 0, buffer.Length);

                    // write pending acks
                    foreach (ulong target in PendingAcks.Keys)
                        foreach (byte[] mailID in PendingAcks[target])
                            stream.Write(mailID, 0, mailID.Length);

                    stream.FlushFinalBlock();
                }


                // save pending ack targets in local file
                string localpath = MailPath + Path.DirectorySeparatorChar + Utilities.CryptFilename(Core, "acktargets");

                if (PendingAcks.Count > 0)
                {
                    using (IVCryptoStream stream = IVCryptoStream.Save(localpath, LocalFileKey))
                    {
                        foreach (ulong target in PendingAcks.Keys)
                            stream.Write(BitConverter.GetBytes(target), 0, 8);

                        stream.FlushFinalBlock();
                    }
                }
                else if (File.Exists(localpath))
                    File.Delete(localpath);

                // make sure to save this after ack targets written, so that it exists when pendingcache_fileaquired triggered
                PendingCache.UpdateLocal(tempPath, key, null);
                
            }
            catch (Exception ex)
            {
                Core.Network.UpdateLog("Profile", "Error saving pending " + ex.Message);
            }
        }

        public string GetCachePath(MailHeader header)
        {
            return CachePath + Path.DirectorySeparatorChar + Utilities.CryptFilename(Core, header.SourceID, header.FileHash);
        }

        public string GetLocalPath(MailHeader header)
        {
            return MailPath + Path.DirectorySeparatorChar + Utilities.CryptFilename(Core, header.SourceID, header.FileHash);
        }

        public void Reply(LocalMail message, string body)
        {
            Core.RunInGuiThread(ShowCompose, message.Header.SourceID, message, "Reply to ", body);
        }

        public void Forward(LocalMail message, string body)
        {
            Core.RunInGuiThread(ShowCompose, 0, message, "Forward to ", body);
        }

        public void DeleteLocal(LocalMail message)
        {
            File.Delete(GetLocalPath(message.Header));
            
            LocalMailbox.LockWriting(delegate()
            {
                int index = LocalMailbox.IndexOfValue(message);

                if(index >= 0)
                    LocalMailbox.RemoveAt(index);

                SaveMailbox = true;
            });
        }

        public string GetNames(List<ulong> list)
        {
            string names = "";

            foreach (ulong id in list)
                names += Core.GetName(id) + ", ";

            names = names.TrimEnd(new char[] { ' ', ',' });

            return names;
        }
    }

    public class CachedPending
    {
        OpVersionedFile File;

        public CachedPending(OpVersionedFile file)
        {
            File = file;
        }

        public VersionedFileHeader Header
        {
            get
            {
                return File.Header;
            }
        }
    }

    public class CachedMail
    {
        public MailHeader Header;
        public byte[]     SignedHeader;
        public bool       Unique;
    }

    public class CachedAck
    {
        public MailAck Ack;
        public byte[]  SignedAck;
        public bool    Unique;

        public CachedAck(byte[] signed, MailAck ack, bool unique)
        {
            SignedAck = signed;
            Ack = ack;
            Unique = unique;
        }
    }

    public class MailIdent
    {
        public const int SIZE = 12;

        public ulong  UserID;
        public byte[] Data;

        public MailIdent()
        { 
        }

        public MailIdent(ulong id, byte[] data)
        {
            UserID  = id;
            Data   = data;
        }

        public byte[] Encode()
        {
            byte[] encoded = new byte[SIZE];

            BitConverter.GetBytes(UserID).CopyTo(encoded, 0);
            Data.CopyTo(encoded, 8);

            return encoded;
        }

        public static MailIdent Decode(byte[] data, int offset)
        {
            MailIdent ident = new MailIdent();

            ident.UserID  = BitConverter.ToUInt64(data, offset);
            ident.Data   = Utilities.ExtractBytes(data, offset + 8, 4);

            return ident;
        }
    }

    public class LocalMail
    {
        public MailInfo Info;

        public ulong From;
        public List<ulong> To = new List<ulong>();
        public List<ulong> CC = new List<ulong>();

        public List<MailFile> Attached = new List<MailFile>();

        public MailHeader Header;
    }
}
