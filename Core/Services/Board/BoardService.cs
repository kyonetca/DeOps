using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using DeOps.Implementation;
using DeOps.Implementation.Dht;
using DeOps.Implementation.Protocol;
using DeOps.Implementation.Protocol.Net;

using DeOps.Services.Trust;
using DeOps.Services.Transfer;
using DeOps.Services.Location;


namespace DeOps.Services.Board
{
    public enum ScopeType {All, High, Low, None };
    public enum BoardSearch { Threads, Time, Post };

    public delegate void PostUpdateHandler(OpPost post);


    public class BoardService : OpService
    {
        public string Name { get { return "Board"; } }
        public uint ServiceID { get { return (uint)ServiceIDs.Board; } }

        public OpCore Core;
        public G2Protocol Protocol;
        public DhtNetwork Network;
        public DhtStore Store;
        TrustService Trust;

        bool Loading = true;
        public string BoardPath;
        byte[] LocalFileKey;

        int PruneSize = 64;

        public List<ulong> SaveHeaders = new List<ulong>();
        public ThreadedDictionary<ulong, OpBoard> BoardMap = new ThreadedDictionary<ulong, OpBoard>();    
        public ThreadedDictionary<ulong, List<int>> WindowMap = new ThreadedDictionary<ulong, List<int>>();


        ThreadedDictionary<int, ushort> SavedReplyCount = new ThreadedDictionary<int, ushort>();
        ThreadedDictionary<ulong, List<PostUID>> DownloadLater = new ThreadedDictionary<ulong, List<PostUID>>();

        public PostUpdateHandler PostUpdate;


        public BoardService(OpCore core )
        {
            Core       = core;
            Network = Core.Network;
            Protocol = Network.Protocol;
            Store    = Network.Store;
            Trust = Core.Trust;

            Core.SecondTimerEvent += Core_SecondTimer;
            Core.MinuteTimerEvent += Core_MinuteTimer;

            Network.CoreStatusChange += new StatusChange(Network_StatusChange);

            Store.StoreEvent[ServiceID, 0] += new StoreHandler(Store_Local);
            Store.ReplicateEvent[ServiceID, 0] += new ReplicateHandler(Store_Replicate);
            Store.PatchEvent[ServiceID, 0] += new PatchHandler(Store_Patch);

            Network.Searches.SearchEvent[ServiceID, 0] += new SearchRequestHandler(Search_Local);

            Core.Transfers.FileSearch[ServiceID, 0] += new FileSearchHandler(Transfers_FileSearch);
            Core.Transfers.FileRequest[ServiceID, 0] += new FileRequestHandler(Transfers_FileRequest);

            if (Core.Sim != null)
                PruneSize = 16;

            LocalFileKey = Core.User.Settings.FileKey;

            BoardPath = Core.User.RootPath + Path.DirectorySeparatorChar + "Data" + Path.DirectorySeparatorChar + ServiceID.ToString();

            if (!Directory.Exists(BoardPath))
                Directory.CreateDirectory(BoardPath);


            // get available board header targets
            string[] directories = Directory.GetDirectories(BoardPath);

            SortedDictionary<ulong, ulong> targets = new SortedDictionary<ulong, ulong>(); // key distance to self, value target

            foreach (string path in directories)
            {
                string dir = Path.GetFileName(path); // gets dir name

                ulong id = BitConverter.ToUInt64(Utilities.FromBase64String(dir), 0);

                targets[Core.UserID ^ id] = id;
            }

            // load closest targets
            int loaded = 0;
            foreach (ulong id in targets.Values)
            {
                LoadHeader(id);

                loaded++;
                if (loaded == PruneSize)
                    break;
            }

            Loading = false;
        }

        public void Dispose()
        {
            Core.SecondTimerEvent -= Core_SecondTimer;
            Core.MinuteTimerEvent -= Core_MinuteTimer;

            Network.CoreStatusChange -= new StatusChange(Network_StatusChange);

            Store.StoreEvent[ServiceID, 0] -= new StoreHandler(Store_Local);
            Store.ReplicateEvent[ServiceID, 0] -= new ReplicateHandler(Store_Replicate);
            Store.PatchEvent[ServiceID, 0] -= new PatchHandler(Store_Patch);

            Network.Searches.SearchEvent[ServiceID, 0] -= new SearchRequestHandler(Search_Local);

            Core.Transfers.FileSearch[ServiceID, 0] -= new FileSearchHandler(Transfers_FileSearch);
            Core.Transfers.FileRequest[ServiceID, 0] -= new FileRequestHandler(Transfers_FileRequest);

        }

        void Core_SecondTimer()
        {
            // save headers, timeout 10 secs
            if (Core.TimeNow.Second % 9 == 0)
                lock (SaveHeaders)
                {
                    foreach (ulong id in SaveHeaders)
                        SaveHeader(id);

                    SaveHeaders.Clear();
                }

            // clean download later map
            if (!Network.Established)
                PruneMap(DownloadLater);
        }

        void Core_MinuteTimer()
        {
            List<ulong> removeBoards = new List<ulong>();

               
            // prune loaded boards
            BoardMap.LockReading(delegate()
            {
                if (BoardMap.Count > PruneSize)
                {
                    List<ulong> localRegion = new List<ulong>();
                    foreach (uint project in Core.Trust.LocalTrust.Links.Keys )
                        localRegion.AddRange(GetBoardRegion(Core.UserID, project, ScopeType.All));

                    foreach (OpBoard board in BoardMap.Values)
                        if (board.UserID != Core.UserID &&
                            !Core.KeepData.SafeContainsKey(board.UserID) &&
                            !WindowMap.SafeContainsKey(board.UserID) &&
                            !localRegion.Contains(board.UserID))
                        {
                            removeBoards.Add(board.UserID);
                        }
                }
            });

            if (removeBoards.Count > 0)
                BoardMap.LockWriting(delegate()
                {
                    while (removeBoards.Count > 0 && BoardMap.Count > PruneSize / 2)
                    {
                        // find furthest id
                        ulong furthest = Core.UserID;

                        foreach (ulong id in removeBoards)
                            if ((id ^ Core.UserID) > (furthest ^ Core.UserID))
                                furthest = id;

                        // remove
                        try
                        {
                            string dir = BoardPath + Path.DirectorySeparatorChar + Utilities.CryptFilename(Core, furthest.ToString());
                            string[] files = Directory.GetFiles(dir);

                            foreach (string path in files)
                                File.Delete(path);

                            Directory.Delete(dir);
                        }
                        catch { }

                        BoardMap.Remove(furthest);
                        removeBoards.Remove(furthest);
                    }
                });
            
        }

        public void SimTest()
        {
            if (Core.InvokeRequired)
            {
                Core.RunInCoreAsync(delegate() { SimTest(); });
                return;
            }

            int choice = Core.RndGen.Next(100);

            ulong user = 0;
            uint project = 0;
            uint parent = 0;
            ScopeType scope = ScopeType.All;

            // post
            if (choice < 25)
            {
                user = Core.UserID;

                int scopeChoice = Core.RndGen.Next(3);

                if(scopeChoice == 0)
                    scope = ScopeType.High;
                else if (scopeChoice == 1)
                    scope = ScopeType.Low;
                else if (scopeChoice == 2)
                    scope = ScopeType.All;
            }

            // reply
            else
            {
                List<ulong> region = GetBoardRegion(Core.UserID, project, ScopeType.All);

                if (region.Count == 0)
                    return;

                ulong target = region[Core.RndGen.Next(region.Count)];

                OpBoard board = null;
                if(BoardMap.SafeTryGetValue(target, out board))
                {
                    user = board.UserID;

                    board.Posts.LockReading(delegate()
                    {
                        if (board.Posts.Count == 0)
                            return;

                        int index = Core.RndGen.Next(board.Posts.Count);

                        int i = 0;
                        foreach (OpPost post in board.Posts.Values)
                        {
                            if (i == index)
                            {
                                if(post.Header.ParentID == 0)
                                    parent = post.Header.PostID;
                                else
                                    parent = post.Header.ParentID;

                                break;
                            }

                            i++;
                        }
                    });
                }

                if (parent == 0)
                    return;
            }

            string subject = Core.TextGen.GenerateSentences(1)[0];
            string message = "";

            message = Core.TextGen.GenerateParagraphs(1)[0];

            PostMessage(user, project, parent, scope, subject, message, TextFormat.Plain, "", new List<AttachedFile>(), null);
        }

        public void SimCleanup()
        {
        }

        private void PruneMap(ThreadedDictionary<ulong, List<PostUID>> map)
        {
            if (map.SafeCount < PruneSize)
                return;

            List<ulong> removeIDs = new List<ulong>();

            map.LockWriting(delegate()
            {
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
            });
        }

        void Network_StatusChange()
        {
            if (Network.Established)
            {
                // republish objects that were not seen on the network during startup
                BoardMap.LockReading(delegate()
                {
                    foreach (OpBoard board in BoardMap.Values)
                        board.Posts.LockReading(delegate()
                        {
                            foreach (OpPost post in board.Posts.Values)
                                if (post.Unique && Network.Routing.InCacheArea(board.UserID))
                                    Store.PublishNetwork(board.UserID, ServiceID, 0, post.SignedHeader);
                        });
                });

                // only download those objects in our local area
                DownloadLater.LockWriting(delegate()
                {
                    foreach (ulong key in DownloadLater.Keys)
                        if (Network.Routing.InCacheArea(key))
                            foreach (PostUID uid in DownloadLater[key])
                                PostSearch(key, uid, 0);

                    DownloadLater.Clear();
                });
            }

            // disconnected, reset cache to unique
            else if(!Network.Responsive)
            {
                BoardMap.LockReading(delegate()
                {
                    foreach (OpBoard board in BoardMap.Values)
                        board.Posts.LockReading(delegate()
                        {
                            foreach (OpPost post in board.Posts.Values)
                                post.Unique = true;
                        });
                });
            }
        }

        public void LoadHeader(ulong id)
        {
            try
            {
                string path = GetTargetDir(id) + Path.DirectorySeparatorChar + Utilities.CryptFilename(Core, "headers" + id.ToString());

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
                                if (embedded.Name == BoardPacket.PostHeader)
                                    Process_PostHeader(null, signed, PostHeader.Decode(embedded));
                        }
                }
            }
            catch(Exception ex)
            {
                Network.UpdateLog("Board", "Could not load header " + id.ToString() + ": " + ex.Message);
            }
        }

        private void SaveHeader(ulong id)
        {
            OpBoard board = GetBoard(id);

            if (board == null)
                return;

            try
            {
                string tempPath = Core.GetTempPath();
                using (IVCryptoStream stream = IVCryptoStream.Save(tempPath, LocalFileKey))
                {
                    board.Posts.LockReading(delegate()
                    {
                        foreach (OpPost post in board.Posts.Values)
                            stream.Write(post.SignedHeader, 0, post.SignedHeader.Length);
                    });

                    stream.FlushFinalBlock();
                }


                string finalPath = GetTargetDir(id) + Path.DirectorySeparatorChar + Utilities.CryptFilename(Core, "headers" + id.ToString());
                File.Delete(finalPath);
                File.Move(tempPath, finalPath);
            }
            catch (Exception ex)
            {
                Network.UpdateLog("Board", "Error saving board headers " + id.ToString() + " " + ex.Message);
            }
        }

        public void PostEdit(OpPost edit)
        {
            // used for archive/restore
            PostHeader copy = edit.Header.Copy();

            copy.Version++;
            copy.EditTime = Core.TimeNow.ToUniversalTime();

            FinishPost(copy);
        }

        public void PostMessage(ulong user, uint project, uint parent, ScopeType scope, string subject, string message, TextFormat format, string quip, List<AttachedFile> files, OpPost edit)
        {
            // post header
            PostHeader header = new PostHeader();
            
            header.Source = Core.User.Settings.KeyPublic;
            header.SourceID = Core.UserID;

            header.Target = Core.KeyMap[user];
            header.TargetID = user;

            header.ParentID = parent;
            header.ProjectID = project;
            
            header.Scope = scope;

            if (edit == null)
            {
                header.Time = Core.TimeNow.ToUniversalTime();
                
                byte[] rnd = new byte[4];
                Core.RndGen.NextBytes(rnd);
                header.PostID = BitConverter.ToUInt32(rnd, 0);
            }
            else
            {
                header.PostID = edit.Header.PostID;
                header.Version = (ushort) (edit.Header.Version + 1);
                header.Time = edit.Header.Time;
                header.EditTime = Core.TimeNow.ToUniversalTime();
            }

            header.FileKey = Utilities.GenerateKey(Core.StrongRndGen, 256);

            // setup temp file
            string tempPath = Core.GetTempPath();
            using (IVCryptoStream stream = IVCryptoStream.Save(tempPath, header.FileKey))
            {
                int written = 0;

                // write post file
                written += Protocol.WriteToFile(new PostInfo(subject, format, quip, Core.RndGen), stream);

                byte[] msgBytes = UTF8Encoding.UTF8.GetBytes(message);
                written += Protocol.WriteToFile(new PostFile("body", msgBytes.Length), stream);

                foreach (AttachedFile attached in files)
                    written += Protocol.WriteToFile(new PostFile(attached.Name, attached.Size), stream);

                stream.WriteByte(0); // end packets
                header.FileStart = (long)written + 1;

                // write files
                stream.Write(msgBytes, 0, msgBytes.Length);

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

                stream.WriteByte(0); // signal last packet

                stream.FlushFinalBlock();
            }

            // finish building header
            Utilities.HashTagFile(tempPath, Network.Protocol, ref header.FileHash, ref header.FileSize);

            string finalPath = GetPostPath(header);
            File.Move(tempPath, finalPath);

            FinishPost(header);
        }

        void FinishPost(PostHeader header)
        {
            if (Core.InvokeRequired)
            {
                Core.RunInCoreBlocked(delegate() { FinishPost(header); });
                return;
            }

            CachePost(new SignedData(Protocol, Core.User.Settings.KeyPair, header), header);

            // publish to network and local region of target
            OpPost post = GetPost(header);

            if (Network.Established)
                Network.Store.PublishNetwork(header.TargetID, ServiceID, 0, GetPost(header).SignedHeader);
            else if (post != null)
                post.Unique = true; // publish when connected
                

            List<LocationData> locations = new List<LocationData>();
            Trust.GetLocs(header.TargetID, header.ProjectID, 1, 1, locations);
            Trust.GetLocs(header.TargetID, header.ProjectID, 0, 1, locations);

            Store.PublishDirect(locations, header.TargetID, ServiceID, 0, GetPost(header).SignedHeader);


            // save right off, dont wait for timer, or sim to be on
            SaveHeader(header.TargetID);
        }


        public string GetPostPath(PostHeader header)
        {
            string targetDir = GetTargetDir(header.TargetID);

            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            byte[] ident = new byte[PostUID.SIZE + header.FileHash.Length];
            new PostUID(header).ToBytes().CopyTo(ident, 0);
            header.FileHash.CopyTo(ident, PostUID.SIZE);

            return targetDir + Path.DirectorySeparatorChar + Utilities.CryptFilename(Core, header.TargetID, ident);
        }

        public string GetTargetDir(ulong id)
        {
            return BoardPath + Path.DirectorySeparatorChar + Utilities.ToBase64String(BitConverter.GetBytes(id));
        }

        void Store_Local(DataReq store)
        {
            // getting published to - search results - patch

            SignedData signed = SignedData.Decode(store.Data);

            if (signed == null)
                return;

            G2Header embedded = new G2Header(signed.Data);

            // figure out data contained
            if (G2Protocol.ReadPacket(embedded))
            {
                if (embedded.Name == BoardPacket.PostHeader)
                    Process_PostHeader(store, signed, PostHeader.Decode(embedded));
            }
        }

        private void Process_PostHeader(DataReq data, SignedData signed, PostHeader header)
        {
            Core.IndexKey(header.SourceID, ref header.Source);
            Core.IndexKey(header.TargetID, ref header.Target);
            
            PostUID uid = new PostUID(header);

            OpPost current = GetPost(header.TargetID, uid);

            // if link loaded
            if (current != null)
            {
                // lower version, send update
                if (header.Version < current.Header.Version)
                {
                    if (data != null && data.Source != null)
                        Store.Send_StoreReq(data.Source, data.LocalProxy, new DataReq(null, header.TargetID, ServiceID, 0, current.SignedHeader));

                    return;
                }

                // higher version
                else if (header.Version > current.Header.Version)
                {
                    CachePost(signed, header);
                }

                // equal version do nothing
            }

            // else load file, set new header after file loaded
            else
                CachePost(signed, header); 
      
        }

        private void CachePost(SignedData signedHeader, PostHeader header)
        {
            if (Core.InvokeRequired)
                Debug.Assert(false);

            if (header.ParentID == 0 && header.SourceID != header.TargetID)
            {
                Network.UpdateLog("Board", "Post made to board other than source's");
                return;
            }

            
            if (!File.Exists(GetPostPath(header)))
            {
                DownloadPost(signedHeader, header);
                return;
            }

            // check if current version loaded
            OpPost post = GetPost(header);
           
            if (post != null && post.Header.Version >= header.Version )
            {
                UpdateGui(post);
                return;
            }

            // put into map
            OpBoard board = GetBoard(header.TargetID);

            if (board == null)
            {
                board = new OpBoard(header.Target);
                BoardMap.SafeAdd(header.TargetID, board);
            }

            PostUID uid = new PostUID(header);

            post = new OpPost();
            post.Header = header;
            post.SignedHeader = signedHeader.Encode(Network.Protocol);
            post.Ident = header.TargetID.GetHashCode() ^ uid.GetHashCode();
            post.Unique = Loading;

            // remove previous version of file, if its different
            OpPost prevPost = board.GetPost(uid);

            if (prevPost != null && GetPostPath(prevPost.Header) != GetPostPath(header))
                try { File.Delete(GetPostPath(prevPost.Header)); }
                catch { }

            board.Posts.SafeAdd(uid,  post);

            // update replies
            if (post.Header.ParentID == 0)
                board.UpdateReplies(post);
            else
            {
                PostUID parentUid = new PostUID(board.UserID, post.Header.ProjectID, post.Header.ParentID);
                OpPost parentPost = board.GetPost(parentUid);

                if (parentPost != null)
                {
                    board.UpdateReplies(parentPost);
                    UpdateGui(post);
                }
            }

            lock (SaveHeaders)
                if (!SaveHeaders.Contains(header.TargetID))
                    SaveHeaders.Add(header.TargetID);

            ushort replies = 0;
            if (SavedReplyCount.SafeTryGetValue(post.Ident, out replies))
            {
                post.Replies = replies;
                SavedReplyCount.SafeRemove(post.Ident);
            }

            UpdateGui(post);

            if (Core.NewsWorthy(header.TargetID, header.ProjectID, true))
                Core.MakeNews(ServiceIDs.Board, "Board updated by " + Core.GetName(header.SourceID), header.SourceID, 0, false);
         
        }

        void DownloadPost(SignedData signed, PostHeader header)
        {
            if (!Utilities.CheckSignedData(header.Source, signed.Data, signed.Signature))
                return;

            FileDetails details = new FileDetails(ServiceID, 0, header.FileHash, header.FileSize, new PostUID(header).ToBytes());

            Core.Transfers.StartDownload(header.TargetID, details, GetPostPath(header), new EndDownloadHandler(EndDownload), new object[] { signed, header });
        }

        private void EndDownload(object[] args)
        {
            SignedData signedHeader = (SignedData)args[0];
            PostHeader header = (PostHeader)args[1];

            CachePost(signedHeader, header);
        }

        bool Transfers_FileSearch(ulong key, FileDetails details)
        {
            if (details.Extra == null || details.Extra.Length < 8)
                return false;

            OpPost post = GetPost(key, PostUID.FromBytes(details.Extra, 0));

            if (post != null && details.Size == post.Header.FileSize && Utilities.MemCompare(details.Hash, post.Header.FileHash))
                return true;

            return false;
        }

        string Transfers_FileRequest(ulong key, FileDetails details)
        {
            if (details.Extra == null || details.Extra.Length < 8)
                return null;

            OpPost post = GetPost(key, PostUID.FromBytes(details.Extra, 0));

            if (post != null && details.Size == post.Header.FileSize && Utilities.MemCompare(details.Hash, post.Header.FileHash))
                return GetPostPath(post.Header);

            return null;
        }

        public OpPost GetPost(PostHeader header)
        {
            return GetPost(header.TargetID, new PostUID(header));
        }

        public OpPost GetPost(ulong target, PostUID uid)
        {
            OpBoard board = GetBoard(target);

            if (board == null)
                return null;

            return board.GetPost(uid);
        }


        int PatchEntrySize = 8 + PostUID.SIZE + 2;

        List<byte[]> Store_Replicate(DhtContact contact)
        {
            List<byte[]> patches = new List<byte[]>();

            
            BoardMap.LockReading(delegate()
            {
                foreach (OpBoard board in BoardMap.Values)
                    if (Network.Routing.InCacheArea(board.UserID))
                    {
                        
                        board.Posts.LockReading(delegate()
                        {
                            foreach (PostUID uid in board.Posts.Keys)
                            {
                                byte[] patch = new byte[PatchEntrySize];

                                BitConverter.GetBytes(board.UserID).CopyTo(patch, 0);

                                uid.ToBytes().CopyTo(patch, 8);
                                BitConverter.GetBytes(board.Posts[uid].Header.Version).CopyTo(patch, 24);

                                patches.Add( patch);
                            }
                        });

                    }
            });

            return patches;
        }

        void Store_Patch(DhtAddress source, byte[] data)
        {
            if (data.Length % PatchEntrySize != 0)
                return;

            int offset = 0;

            for (int i = 0; i < data.Length; i += PatchEntrySize)
            {
                ulong user = BitConverter.ToUInt64(data, i);
                PostUID uid = PostUID.FromBytes(data, i + 8);
                ushort version = BitConverter.ToUInt16(data, i + 24);

                offset += PatchEntrySize;

                if (!Network.Routing.InCacheArea(user))
                    continue;

                OpPost post = GetPost(user, uid);

                if (post != null)
                {
                    // remote version is lower, send update
                    if (post.Header.Version > version)
                    {
                        Store.Send_StoreReq(source, null, new DataReq(null, user, ServiceID, 0, post.SignedHeader));
                        continue;
                    }
                        
                    // version equal,  pass
                    post.Unique = false; // network has current or newer version

                    if (post.Header.Version == version)
                        continue;

                    // else our version is old, download below
                }

                // download cause we dont have it or its a higher version 
                if (Network.Established)
                    Network.Searches.SendDirectRequest(source, user, ServiceID, 0, uid.ToBytes());
                else
                {
                    List<PostUID> list = null;
                    if (!DownloadLater.SafeTryGetValue(user, out list))
                    {
                        list = new List<PostUID>();
                        DownloadLater.SafeAdd(user, list);
                    }

                    list.Add(uid);
                }
            }
        }



        

        const int TheadSearch_ParamsSize = 9;   // type/project/parent  1 + 4 + 4
        const int TheadSearch_ResultsSize = 20; // UID/version/replies 16 + 2 + 2

        public void ThreadSearch(ulong target, uint project, uint parent)
        {
            if (Core.InvokeRequired)
            {
                Core.RunInCoreAsync(delegate() { ThreadSearch(target, project, parent); });
                return;
            }

            byte[] parameters = new byte[TheadSearch_ParamsSize];
            parameters[0] = (byte) BoardSearch.Threads;
            BitConverter.GetBytes(project).CopyTo(parameters, 1);
            BitConverter.GetBytes(parent).CopyTo(parameters, 5);

            DhtSearch search = Network.Searches.Start(target, "Board:Thread", ServiceID, 0, parameters, Search_FoundThread);

            if (search == null)
                return;

            search.TargetResults = 50;
        }

        void Search_FoundThread(DhtSearch search, DhtAddress source, byte[] data)
        {
            if (data.Length < TheadSearch_ResultsSize)
                return;

            PostUID uid = PostUID.FromBytes(data, 0);
            ushort version = BitConverter.ToUInt16(data, 16);
            ushort replies = BitConverter.ToUInt16(data, 18);

            OpPost post = GetPost(search.TargetID, uid);

            if (post != null)
            {
                if (post.Replies < replies)
                {
                    post.Replies = replies;
                    UpdateGui(post);
                }

                // if we have current version, pass, else download
                if (post.Header.Version >= version)
                    return;
            }

            PostSearch(search.TargetID, uid, version);

            // if parent save replies value
            if (replies != 0)
            {
                int hash = search.TargetID.GetHashCode() ^ uid.GetHashCode();

                ushort savedReplies = 0;
                if (SavedReplyCount.SafeTryGetValue(hash, out savedReplies))
                    if (savedReplies < replies)
                        SavedReplyCount.SafeAdd(hash, replies);
            }
        }

        const int TimeSearch_ParamsSize = 13;   // type/project/time 1 + 4 + 8
        const int TimeSearch_ResultsSize = 18;  // UID/version 16 + 2

        public void TimeSearch(ulong target, uint project, DateTime time)
        {
            if (Core.InvokeRequired)
            {
                Core.RunInCoreAsync(delegate() { TimeSearch(target, project, time); });
                return;
            }


            byte[] parameters = new byte[TimeSearch_ParamsSize];
            parameters[0] = (byte)BoardSearch.Time;
            BitConverter.GetBytes(project).CopyTo(parameters, 1);
            BitConverter.GetBytes(time.ToBinary()).CopyTo(parameters, 5);

            DhtSearch search = Network.Searches.Start(target, "Board:Time", ServiceID, 0, parameters, Search_FoundTime);

            if (search == null)
                return;

            search.TargetResults = 50;
        }

        void Search_FoundTime(DhtSearch search, DhtAddress source, byte[] data)
        {
            OpBoard board = GetBoard(search.TargetID);

            if (data.Length < TheadSearch_ResultsSize)
                return;

            PostUID uid = PostUID.FromBytes(data, 0);
            ushort version = BitConverter.ToUInt16(data, 16);

            OpPost post = GetPost(search.TargetID, uid);

            if (post != null)
                if (post.Header.Version >= version)
                    return;

            PostSearch(search.TargetID, uid, version);
        }

        const int PostSearch_ParamsSize = 19;   // type/uid/version  1 + 16 + 2

        private void PostSearch(ulong target, PostUID uid, ushort version)
        {
            if (Core.InvokeRequired)
            {
                Core.RunInCoreAsync(delegate() { PostSearch(target, uid, version); });
                return;
            }

            byte[] parameters = new byte[PostSearch_ParamsSize];
            parameters[0] = (byte)BoardSearch.Post;
            uid.ToBytes().CopyTo(parameters, 1);
            BitConverter.GetBytes(version).CopyTo(parameters, 17);

            DhtSearch search = Core.Network.Searches.Start(target, "Board:Post", ServiceID, 0, parameters, Search_FoundPost);

            if (search == null)
                return;

            search.TargetResults = 2;
        }

        void Search_FoundPost(DhtSearch search, DhtAddress source, byte[] data)
        {
            Store_Local(new DataReq(source, search.TargetID, ServiceID, 0, data));
        }

        void Search_Local(ulong key, byte[] parameters, List<byte[]> results)
        {
            OpBoard board = GetBoard(key);

            if (board == null || parameters == null)
                return;

            // thread search
            if (parameters[0] == (byte)BoardSearch.Threads)
            {
                if (parameters.Length < TheadSearch_ParamsSize)
                    return;

                uint project = BitConverter.ToUInt32(parameters, 1);
                uint parent = BitConverter.ToUInt32(parameters, 5);

                board.Posts.LockReading(delegate()
                {
                    foreach (OpPost post in board.Posts.Values)
                        if (post.Header.ProjectID == project)
                            if ((parent == 0 && post.Header.ParentID == 0) || // searching for top level threads
                                (parent == post.Header.ParentID)) // searching for posts under particular thread
                            {
                                byte[] result = new byte[TheadSearch_ResultsSize];
                                new PostUID(post.Header).ToBytes().CopyTo(result, 0);
                                BitConverter.GetBytes(post.Header.Version).CopyTo(result, 16);
                                BitConverter.GetBytes(post.Replies).CopyTo(result, 18);

                                results.Add(result);
                            }
                });
            }

            // time search
            else if (parameters[0] == (byte)BoardSearch.Time)
            {
                if (parameters.Length < TimeSearch_ParamsSize)
                    return;

                uint project = BitConverter.ToUInt32(parameters, 1);
                DateTime time = DateTime.FromBinary(BitConverter.ToInt64(parameters, 5));

                board.Posts.LockReading(delegate()
                {
                    foreach (OpPost post in board.Posts.Values)
                        if (post.Header.ProjectID == project && post.Header.Time > time)
                        {
                            byte[] result = new byte[TimeSearch_ResultsSize];
                            new PostUID(post.Header).ToBytes().CopyTo(result, 0);
                            BitConverter.GetBytes(post.Header.Version).CopyTo(result, 16);

                            results.Add(result);
                        }
                });
            }

            // post search
            else if (parameters[0] == (byte)BoardSearch.Post)
            {
                if (parameters.Length < PostSearch_ParamsSize)
                    return;

                PostUID uid = PostUID.FromBytes(parameters, 1);
                ushort version = BitConverter.ToUInt16(parameters, 17);

                OpPost post = GetPost(key, uid);
                if (post != null)
                    if (post.Header.Version == version)
                        results.Add(post.SignedHeader);
            }
        }

        public string GetPostTitle(OpPost post)
        {
            // loads info when first demanded, cached afterwards
            if (post.Info == null)
                try
                {
                    string path = GetPostPath(post.Header);
                    if (!File.Exists(path))
                        return "";

                    post.Attached = new List<PostFile>();

                    using (TaggedStream file = new TaggedStream(path, Network.Protocol))
                    using (IVCryptoStream crypto = IVCryptoStream.Load(file, post.Header.FileKey))
                    {
                        PacketStream stream = new PacketStream(crypto, Network.Protocol, FileAccess.Read);

                        G2Header root = null;

                        while (stream.ReadPacket(ref root))
                        {
                            if (root.Name == BoardPacket.PostInfo)
                                post.Info = PostInfo.Decode(root);

                            else if (root.Name == BoardPacket.PostFile)
                                post.Attached.Add(PostFile.Decode(root));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Network.UpdateLog("Board", "Could not load post " + post.Header.SourceID.ToString() + ": " + ex.Message);
                }

            if (post.Info == null)
                return "";


            if (post.Header.ParentID == 0)
                return post.Info.Subject;
            else
                return post.Info.Quip;
        }

        public OpBoard GetBoard(ulong key)
        {
            OpBoard board = null;

            BoardMap.SafeTryGetValue(key, out board);

            return board;
        }


        public void LoadView(int viewId, ulong userId)
        {
            WindowMap.LockWriting(delegate()
            {
                if (!WindowMap.ContainsKey(userId))
                    WindowMap[userId] = new List<int>();

                WindowMap[userId].Add(viewId);
            });
        }

        public void UnloadView(int viewId, ulong userId)
        {
            WindowMap.LockWriting(delegate()
           {
               if (!WindowMap.ContainsKey(userId))
                   return;

               WindowMap[userId].Remove(viewId);

               if (WindowMap[userId].Count == 0)
                   WindowMap.Remove(userId);
           });
        }

        public List<ulong> GetBoardRegion(ulong id, uint project, ScopeType scope)
        {
            List<ulong> targets = new List<ulong>();

            targets.Add(id); // need to include self in high and low scopes, for re-searching, onlinkupdate purposes

            OpLink link = Trust.GetLink(id, project);

            if (link == null)
                return targets;


            // higher
            if (scope != ScopeType.Low)
            {
                // if we're in loop, get our 'adjacents' who are really uplinks
                if (link.LoopRoot != null)
                {
                    targets.AddRange( Trust.GetUplinkIDs(id, project) );
                }

                // get parent and children of parent
                else
                {
                    OpLink parent = link.GetHigher(true);

                    if (parent != null)
                    {
                        targets.Add(parent.UserID);

                        targets.AddRange(Trust.GetDownlinkIDs(parent.UserID, project, 1));

                        targets.Remove(id); // remove self
                    }
                }
            }

            // lower - get children of self
            if (scope != ScopeType.High)
                targets.AddRange(Trust.GetDownlinkIDs(id, project, 1));


            return targets;
        }

        public void LoadRegion(ulong user, uint project)
        {
            if (Core.InvokeRequired)
            {
                Core.RunInCoreAsync(delegate() { LoadRegion(user, project); });
                return;
            }

            // get all boards in local region
            List<ulong> region = GetBoardRegion(user, project, ScopeType.All);

            foreach (ulong id in region)
            {
                OpBoard board = GetBoard(id);

                if (board == null)
                    LoadHeader(id); // updateinterface called in processheader
            }

            // searches
            foreach (ulong id in region)
                SearchBoard(id, project);
        }

        public void SearchBoard(ulong target, uint project)
        {
            bool fullSearch = true;

            OpBoard board = GetBoard(target);

            DateTime refresh = default(DateTime);
            if (board != null)
                if (board.LastRefresh.SafeTryGetValue(project, out refresh))
                    fullSearch = false;


            if (fullSearch)
                ThreadSearch(target, project, 0); 

            // search for all theads posted since refresh, with an hour buffer
            else
                TimeSearch(target, project, refresh.AddHours(-1));


            if (board != null)
                board.LastRefresh.SafeAdd(project, Core.TimeNow);
        }

        public void LoadThread(OpPost parent)
        {
            OpBoard board = GetBoard(parent.Header.TargetID);

            if (board == null)
                return;

            // have all replies fire an update
            board.Posts.LockReading(delegate()
            {
               foreach (OpPost post in board.Posts.Values)
                   if (post.Header.ProjectID == parent.Header.ProjectID &&
                       post.Header.ParentID == parent.Header.PostID)
                       UpdateGui(post);
            });


            // do search for thread
            ThreadSearch(board.UserID, parent.Header.ProjectID, parent.Header.PostID);
        }


        public void UpdateGui(OpPost post)
        {
            if (PostUpdate != null)
                Core.RunInGuiThread(PostUpdate, post);
        }

        // cant delete post because we dont control them with any master header
        // future - local board file that consists of deleted post ids
        public void Archive(OpPost post, bool state)
        {
            post.Header.Archived = state;

            PostEdit(post);

            UpdateGui(null);
        }
    }

    public class PostUID
    {
        public const int SIZE = 16;

        public ulong SenderID;
        public uint  ProjectID;
        public uint  PostID;

        public PostUID()
        {
        }

        public PostUID(PostHeader header)
        {
            SenderID = header.SourceID ;
            ProjectID = header.ProjectID;
            PostID = header.PostID;
        }

        public PostUID(ulong sender, uint project, uint post)
        {
            SenderID = sender;
            ProjectID = project;
            PostID = post;
        }

        public byte[] ToBytes()
        {
            byte[] data = new byte[SIZE];

            BitConverter.GetBytes(SenderID).CopyTo(data, 0);
            BitConverter.GetBytes(ProjectID).CopyTo(data, 8);
            BitConverter.GetBytes(PostID).CopyTo(data, 12);

            return data;
        }

        public static PostUID FromBytes(byte[] data, int offset)
        {
            PostUID uid = new PostUID();

            uid.SenderID = BitConverter.ToUInt64(data, offset + 0);
            uid.ProjectID = BitConverter.ToUInt32(data, offset + 8);
            uid.PostID = BitConverter.ToUInt32(data, offset + 12);

            return uid;
        }

        public override string ToString()
        {
            return Utilities.ToBase64String(ToBytes());
        }

        public override int GetHashCode()
        {
            return SenderID.GetHashCode() ^ ProjectID.GetHashCode() ^ PostID.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            PostUID compare = obj as PostUID;
            if (compare == null)
                return false;

            if (SenderID == compare.SenderID && ProjectID == compare.ProjectID && PostID == compare.PostID)
                return true;

            return false;
        }
    }

    public class OpBoard
    {
        public ulong UserID;
        public byte[] Key;    // make sure reference is the same as main key list

        public ThreadedDictionary<PostUID, OpPost> Posts = new ThreadedDictionary<PostUID, OpPost>();
        public ThreadedDictionary<uint, DateTime> LastRefresh = new ThreadedDictionary<uint, DateTime>();
        

        public OpBoard(byte[] key)
        {
            Key = key;
            UserID = Utilities.KeytoID(key);
        }

        public OpPost GetPost(PostUID uid)
        {
            OpPost post = null;

            Posts.SafeTryGetValue(uid, out post);

            return post;
        }

        public void UpdateReplies(OpPost parent)
        {
            // count replies to post, if greater than variable set, overwrite

            ushort replies = 0;

            Posts.LockReading(delegate()
            {
                foreach (OpPost post in Posts.Values)
                    if (post.Header.ParentID == parent.Header.PostID)
                        replies++;
            });

            if (replies > parent.Replies)
                parent.Replies = replies;
        }

    }

    public class OpPost
    {
        public int Ident;
        public bool Unique;

        public PostHeader Header;
        public byte[] SignedHeader;

        public PostInfo Info;
        public List<PostFile> Attached;

        public ushort Replies; // only parent uses this variable
    }
}
