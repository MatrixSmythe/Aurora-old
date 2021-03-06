/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Reflection;
using log4net;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.Region.CoreModules.World.Land
{
    /// <summary>
    /// Keeps track of a specific piece of land's information
    /// </summary>
    public class LandObject : ILandObject
    {
        #region Member Variables

        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        #pragma warning disable 0429
        private const int landArrayMax = ((int)((int)Constants.RegionSize / 4) >= 64) ? (int)((int)Constants.RegionSize / 4) : 64;
        #pragma warning restore 0429
        private bool[,] m_landBitmap = new bool[landArrayMax,landArrayMax];

        private int m_lastSeqId = 0;

        protected LandData m_landData = new LandData();
        protected Scene m_scene;
        protected List<SceneObjectGroup> primsOverMe = new List<SceneObjectGroup>();
        public List<SceneObjectGroup> PrimsOverMe
        {
            get { return primsOverMe; }
            set { primsOverMe = value; }
        }

        public bool[,] LandBitmap
        {
            get { return m_landBitmap; }
            set { m_landBitmap = value; }
        }

        #endregion

        #region ILandObject Members

        public LandData LandData
        {
            get { return m_landData; }

            set
            {
                //Fix the land data HERE
                if (m_scene != null) //Not sure that this ever WILL be null... but we'll be safe...
                    value.Maturity = m_scene.RegionInfo.RegionSettings.Maturity;
                m_landData = value; 
            }
        }

        public UUID RegionUUID
        {
            get { return m_scene.RegionInfo.RegionID; }
        }

        #region Constructors

        public LandObject(UUID owner_id, bool is_group_owned, Scene scene)
        {
            m_scene = scene;
            LandData.Maturity = m_scene.RegionInfo.RegionSettings.Maturity;
            LandData.OwnerID = owner_id;
            if (is_group_owned)
                LandData.GroupID = owner_id;
            else
                LandData.GroupID = UUID.Zero;
            LandData.IsGroupOwned = is_group_owned;

            LandData.RegionID = scene.RegionInfo.RegionID;
            LandData.RegionHandle = scene.RegionInfo.RegionHandle;

            //We don't set up the InfoID here... it will just be overwriten
        }

        public void SetInfoID()
        {
            //Make the InfoUUID for this parcel
            uint x = (uint)LandData.UserLocation.X, y = (uint)LandData.UserLocation.Y;
            findPointInParcel(this, ref x, ref y); // find a suitable spot
            LandData.InfoUUID = Util.BuildFakeParcelID(LandData.RegionHandle, x, y);
        }

        // this is needed for non-convex parcels (example: rectangular parcel, and in the exact center
        // another, smaller rectangular parcel). Both will have the same initial coordinates.
        private void findPointInParcel(ILandObject land, ref uint refX, ref uint refY)
        {
            // the point we started with already is in the parcel
            if (land.ContainsPoint((int)refX, (int)refY)) return;

            // ... otherwise, we have to search for a point within the parcel
            uint startX = (uint)land.LandData.AABBMin.X;
            uint startY = (uint)land.LandData.AABBMin.Y;
            uint endX = (uint)land.LandData.AABBMax.X;
            uint endY = (uint)land.LandData.AABBMax.Y;

            // default: center of the parcel
            refX = (startX + endX) / 2;
            refY = (startY + endY) / 2;
            // If the center point is within the parcel, take that one
            if (land.ContainsPoint((int)refX, (int)refY)) return;

            // otherwise, go the long way.
            for (uint y = startY; y <= endY; ++y)
            {
                for (uint x = startX; x <= endX; ++x)
                {
                    if (land.ContainsPoint((int)x, (int)y))
                    {
                        // found a point
                        refX = x;
                        refY = y;
                        return;
                    }
                }
            }
        }

        #endregion

        #region Member Functions

        #region General Functions

        /// <summary>
        /// Checks to see if this land object contains a point
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns>Returns true if the piece of land contains the specified point</returns>
        public bool ContainsPoint(int x, int y)
        {
            if (x >= 0 && y >= 0 && x < Constants.RegionSize && y < Constants.RegionSize)
            {
                return (LandBitmap[x / 4, y / 4] == true);
            }
            else
            {
                return false;
            }
        }

        public ILandObject Copy()
        {
            ILandObject newLand = new LandObject(LandData.OwnerID, LandData.IsGroupOwned, m_scene);

            //Place all new variables here!
            newLand.LandBitmap = (bool[,]) (LandBitmap.Clone());
            newLand.LandData = LandData.Copy();

            return newLand;
        }

        public int GetParcelMaxPrimCount(ILandObject thisObject)
        {
            // Normal Calculations
            return (int)Math.Round(((float)LandData.Area / 65536.0f) * (float)m_scene.RegionInfo.ObjectCapacity * (float)m_scene.RegionInfo.RegionSettings.ObjectBonus);
        }

        public int GetSimulatorMaxPrimCount(ILandObject thisObject)
        {
            return m_scene.RegionInfo.ObjectCapacity;
        }

        #endregion

        #region Packet Request Handling

        public void SendLandProperties(int sequence_id, bool snap_selection, int request_result, IClientAPI remote_client)
        {
            IEstateModule estateModule = m_scene.RequestModuleInterface<IEstateModule>();
            uint regionFlags = 336723974 & ~((uint)(RegionFlags.AllowLandmark | RegionFlags.AllowSetHome));
            if (estateModule != null)
                regionFlags = estateModule.GetRegionFlags();

            // In a perfect world, this would have worked.
            //
//            if ((landData.Flags & (uint)ParcelFlags.AllowLandmark) != 0)
//                regionFlags |=  (uint)RegionFlags.AllowLandmark;
//            if (landData.OwnerID == remote_client.AgentId)
//                regionFlags |=  (uint)RegionFlags.AllowSetHome;

            int seq_id;
            if (snap_selection && (sequence_id == 0))
            {
                seq_id = m_lastSeqId;
            }
            else
            {
                seq_id = sequence_id;
                m_lastSeqId = seq_id;
            }

            remote_client.SendLandProperties(seq_id,
                    snap_selection, request_result, LandData,
                    (float)m_scene.RegionInfo.RegionSettings.ObjectBonus,
                    GetParcelMaxPrimCount(this),
                    GetSimulatorMaxPrimCount(this), regionFlags);
        }

        public void UpdateLandProperties(LandUpdateArgs args, IClientAPI remote_client)
        {
            if (m_scene.Permissions.CanEditParcel(remote_client.AgentId, this) &&
                m_scene.RegionInfo.EstateSettings.AllowParcelChanges)
            {
                try
                {
                    bool snap_selection = false;
                    LandData newData = LandData.Copy();

                    if (args.AuthBuyerID != newData.AuthBuyerID || args.SalePrice != newData.SalePrice)
                    {
                        if (m_scene.Permissions.CanSellParcel(remote_client.AgentId, this) &&
                            m_scene.RegionInfo.RegionSettings.AllowLandResell)
                        {
                            newData.AuthBuyerID = args.AuthBuyerID;
                            newData.SalePrice = args.SalePrice;
                            snap_selection = true;
                        }
                        else
                            remote_client.SendAlertMessage("Permissions: You cannot set this parcel for sale");
                    }

                    newData.Category = args.Category;
                    newData.Description = args.Desc;
                    newData.GroupID = args.GroupID;
                    newData.LandingType = args.LandingType;
                    newData.MediaAutoScale = args.MediaAutoScale;
                    newData.MediaID = args.MediaID;
                    newData.MediaURL = args.MediaURL;
                    newData.MusicURL = args.MusicURL;
                    newData.Name = args.Name;

                    if (!m_scene.RegionInfo.RegionSettings.AllowDamage &&
                        ((args.ParcelFlags & (uint)ParcelFlags.AllowDamage) == (uint)ParcelFlags.AllowDamage))
                        //Vanquish damage as per estate settings!
                        args.ParcelFlags &= ~(uint)ParcelFlags.AllowDamage;

                    if (m_scene.RegionInfo.RegionSettings.BlockFly &&
                        ((args.ParcelFlags & (uint)ParcelFlags.AllowFly) == (uint)ParcelFlags.AllowFly))
                        //Vanquish flying as per estate settings!
                        args.ParcelFlags &= ~(uint)ParcelFlags.AllowFly;

                    if (m_scene.RegionInfo.RegionSettings.RestrictPushing &&
                        ((args.ParcelFlags & (uint)ParcelFlags.RestrictPushObject) == (uint)ParcelFlags.RestrictPushObject))
                        //Vanquish pushing as per estate settings!
                        args.ParcelFlags &= ~(uint)ParcelFlags.RestrictPushObject;

                    if (!m_scene.RegionInfo.EstateSettings.AllowLandmark &&
                        ((args.ParcelFlags & (uint)ParcelFlags.AllowLandmark) == (uint)ParcelFlags.AllowLandmark))
                        //Vanquish landmarks as per estate settings!
                        args.ParcelFlags &= ~(uint)ParcelFlags.AllowLandmark;

                    if (m_scene.RegionInfo.RegionSettings.BlockShowInSearch &&
                        ((args.ParcelFlags & (uint)ParcelFlags.ShowDirectory) == (uint)ParcelFlags.ShowDirectory))
                        //Vanquish show in search as per estate settings!
                        args.ParcelFlags &= ~(uint)ParcelFlags.ShowDirectory;

                    newData.Flags = args.ParcelFlags;
                    newData.PassHours = args.PassHours;
                    newData.PassPrice = args.PassPrice;
                    newData.SnapshotID = args.SnapshotID;
                    newData.UserLocation = args.UserLocation;
                    newData.UserLookAt = args.UserLookAt;
                    newData.MediaType = args.MediaType;
                    newData.MediaDescription = args.MediaDescription;
                    newData.MediaWidth = args.MediaWidth;
                    newData.MediaHeight = args.MediaHeight;
                    newData.MediaLoop = args.MediaLoop;
                    newData.ObscureMusic = args.ObscureMusic;
                    newData.ObscureMedia = args.ObscureMedia;

                    m_scene.LandChannel.UpdateLandObject(LandData.LocalID, newData);

                    SendLandUpdateToAvatarsOverMe(snap_selection);
                }
                catch(Exception ex)
                {
                    m_log.Warn("[LAND]: Error updating land object " + this.LandData.Name + " in region " + this.m_scene.RegionInfo.RegionName + " : " + ex.ToString());
                }
            }
            else
            {
                remote_client.SendAlertMessage("You do not have permissions to edit this land");
            }
        }

        public void UpdateLandSold(UUID avatarID, UUID groupID, bool groupOwned, uint AuctionID, int claimprice, int area)
        {
            LandData newData = LandData.Copy();
            newData.OwnerID = avatarID;
            newData.GroupID = groupID;
            newData.IsGroupOwned = groupOwned;
            //newData.auctionID = AuctionID;
            newData.ClaimDate = Util.UnixTimeSinceEpoch();
            newData.ClaimPrice = claimprice;
            newData.SalePrice = 0;
            newData.AuthBuyerID = UUID.Zero;
            newData.Flags &= ~(uint) (ParcelFlags.ForSale | ParcelFlags.ForSaleObjects | ParcelFlags.SellParcelObjects | ParcelFlags.ShowDirectory);
            m_scene.LandChannel.UpdateLandObject(LandData.LocalID, newData);

            SendLandUpdateToAvatarsOverMe(true);
        }

        public void DeedToGroup(UUID groupID)
        {
            LandData newData = LandData.Copy();
            newData.OwnerID = groupID;
            newData.GroupID = groupID;
            newData.IsGroupOwned = true;

            // Reset show in directory flag on deed
            newData.Flags &= ~(uint) (ParcelFlags.ForSale | ParcelFlags.ForSaleObjects | ParcelFlags.SellParcelObjects | ParcelFlags.ShowDirectory);

            m_scene.LandChannel.UpdateLandObject(LandData.LocalID, newData);
        }

        public bool IsEitherBannedOrRestricted(UUID avatar)
        {
            if (IsBannedFromLand(avatar))
            {
                return true;
            }
            else if (IsRestrictedFromLand(avatar))
            {
                return true;
            }
            return false;
        }

        public bool IsBannedFromLand(UUID avatar)
        {
            if (m_scene.Permissions.IsAdministrator(avatar))
                return false;

            if ((LandData.Flags & (uint) ParcelFlags.UseBanList) > 0)
            {
                ParcelManager.ParcelAccessEntry entry = new ParcelManager.ParcelAccessEntry();
                entry.AgentID = avatar;
                entry.Flags = AccessList.Ban;
                entry = LandData.ParcelAccessList.Find(delegate(ParcelManager.ParcelAccessEntry pae)
                {
                    if (entry.AgentID == pae.AgentID && entry.Flags == pae.Flags)
                        return true;
                    return false;
                });

                //See if they are on the list, but make sure the owner isn't banned
                if (entry.AgentID == avatar && LandData.OwnerID != avatar)
                {
                    //They are banned, so lets send them a notice about this parcel
                    return true;
                }
            }
            return false;
        }

        public bool IsRestrictedFromLand(UUID avatar)
        {
            if (m_scene.Permissions.IsAdministrator(avatar))
                return false;

            if ((LandData.Flags & (uint) ParcelFlags.UseAccessList) > 0)
            {
                ParcelManager.ParcelAccessEntry entry = new ParcelManager.ParcelAccessEntry();
                entry.AgentID = avatar;
                entry.Flags = AccessList.Access;
                entry.Time = new DateTime();
                entry = LandData.ParcelAccessList.Find(delegate(ParcelManager.ParcelAccessEntry pae)
                {
                    if (entry.AgentID == pae.AgentID && entry.Flags == pae.Flags)
                        return true;
                    return false;
                });

                //If they are not on the access list and are not the owner
                if (entry.AgentID == avatar && LandData.OwnerID != avatar)
                {
                    if ((LandData.Flags & (uint)ParcelFlags.UseAccessGroup) > 0)
                    {
                        ScenePresence SP = m_scene.GetScenePresence(avatar);
                        if (SP != null && LandData.GroupID == SP.ControllingClient.ActiveGroupId)
                        {
                            //They are a part of the group, let them in
                            return false;
                        }
                        else
                        {
                            //They are not allowed in this parcel, but not banned, so lets send them a notice about this parcel
                            return true;
                        }
                    }
                    else
                    {
                        //No group checking, not on the access list, restricted
                        return true;
                    }
                }
                else
                {
                    //If it does, we need to check the time
                    entry = LandData.ParcelAccessList.Find(delegate (ParcelManager.ParcelAccessEntry item)
                    {
                        if(item.AgentID == entry.AgentID && item.Flags == entry.Flags)
                            return true;
                        return false;
                    });

                    if (entry.Time.Ticks < DateTime.Now.Ticks)
                    {
                        //Time expired, remove them
                        LandData.ParcelAccessList.Remove(entry);
                        return true;
                    }
                }
            }
            return false;
        }

        public void SendLandUpdateToClient(IClientAPI remote_client)
        {
            SendLandProperties(0, false, 0, remote_client);
        }

        public void SendLandUpdateToClient(bool snap_selection, IClientAPI remote_client)
        {
            SendLandProperties(0, snap_selection, 0, remote_client);
        }

        public void SendLandUpdateToAvatarsOverMe()
        {
            SendLandUpdateToAvatarsOverMe(false);
        }

        public void SendLandUpdateToAvatarsOverMe(bool snap_selection)
        {
            m_scene.ForEachScenePresence(delegate(ScenePresence avatar)
            {
                if (avatar.IsChildAgent)
                    return;

                ILandObject over = null;
                try
                {
                    over =
                        m_scene.LandChannel.GetLandObject(Util.Clamp<int>((int)Math.Round(avatar.AbsolutePosition.X), 0, ((int)Constants.RegionSize - 1)),
                                                          Util.Clamp<int>((int)Math.Round(avatar.AbsolutePosition.Y), 0, ((int)Constants.RegionSize - 1)));
                }
                catch (Exception)
                {
                    m_log.Warn("[LAND]: " + "unable to get land at x: " + Util.Clamp<int>((int)Math.Round(avatar.AbsolutePosition.X), 0, ((int)Constants.RegionSize - 1)) + " y: " +
                               Util.Clamp<int>((int)Math.Round(avatar.AbsolutePosition.Y), 0, ((int)Constants.RegionSize - 1)));
                }

                if (over != null)
                {
                    if (over.LandData.LocalID == LandData.LocalID)
                    {
                        if (((over.LandData.Flags & (uint)ParcelFlags.AllowDamage) != 0) &&
                            m_scene.RegionInfo.RegionSettings.AllowDamage)
                            avatar.Invulnerable = false;
                        else
                            avatar.Invulnerable = true;

                        SendLandUpdateToClient(snap_selection, avatar.ControllingClient);
                    }
                }
                else
                {
                    m_log.Warn("[LAND]: " + "unable to get land at x: " + Util.Clamp<int>((int)Math.Round(avatar.AbsolutePosition.X), 0, ((int)Constants.RegionSize - 1)) + " y: " +
                               Util.Clamp<int>((int)Math.Round(avatar.AbsolutePosition.Y), 0, ((int)Constants.RegionSize - 1)));
                }
            });
        }

        #endregion

        #region AccessList Functions

        public List<UUID>  CreateAccessListArrayByFlag(AccessList flag)
        {
            List<UUID> list = new List<UUID>();
            foreach (ParcelManager.ParcelAccessEntry entry in LandData.ParcelAccessList)
            {
                if (entry.Flags == flag)
                {
                   list.Add(entry.AgentID);
                }
            }
            if (list.Count == 0)
            {
                list.Add(UUID.Zero);
            }

            return list;
        }

        public void SendAccessList(UUID agentID, UUID sessionID, uint flags, int sequenceID,
                                   IClientAPI remote_client)
        {

            if (flags == (uint) AccessList.Access || flags == (uint) AccessList.Both)
            {
                List<UUID> avatars = CreateAccessListArrayByFlag(AccessList.Access);
                remote_client.SendLandAccessListData(avatars,(uint) AccessList.Access,LandData.LocalID);
            }

            if (flags == (uint) AccessList.Ban || flags == (uint) AccessList.Both)
            {
                List<UUID> avatars = CreateAccessListArrayByFlag(AccessList.Ban);
                remote_client.SendLandAccessListData(avatars, (uint)AccessList.Ban, LandData.LocalID);
            }
        }

        public void UpdateAccessList(uint flags, List<ParcelManager.ParcelAccessEntry> entries, IClientAPI remote_client)
        {
            LandData newData = LandData.Copy();

            if (entries.Count == 1 && entries[0].AgentID == UUID.Zero)
            {
                entries.Clear();
            }

            List<ParcelManager.ParcelAccessEntry> toRemove = new List<ParcelManager.ParcelAccessEntry>();
            foreach (ParcelManager.ParcelAccessEntry entry in newData.ParcelAccessList)
            {
                if (entry.Flags == (AccessList)flags)
                {
                    toRemove.Add(entry);
                }
            }

            foreach (ParcelManager.ParcelAccessEntry entry in toRemove)
            {
                newData.ParcelAccessList.Remove(entry);
            }
            foreach (ParcelManager.ParcelAccessEntry entry in entries)
            {
                ParcelManager.ParcelAccessEntry temp = new ParcelManager.ParcelAccessEntry();
                temp.AgentID = entry.AgentID;
                temp.Time = DateTime.MaxValue; //Pointless? NO.
                temp.Flags = (AccessList)flags;

                if (!newData.ParcelAccessList.Contains(temp))
                {
                    newData.ParcelAccessList.Add(temp);
                }
            }

            m_scene.LandChannel.UpdateLandObject(LandData.LocalID, newData);
        }

        #endregion

        #region Update Functions

        public void UpdateLandBitmapByteArray()
        {
            LandData.Bitmap = ConvertLandBitmapToBytes();
        }

        /// <summary>
        /// Update all settings in land such as area, bitmap byte array, etc
        /// </summary>
        public void ForceUpdateLandInfo()
        {
            UpdateAABBAndAreaValues();
            UpdateLandBitmapByteArray();
        }

        public void SetLandBitmapFromByteArray()
        {
            LandBitmap = ConvertBytesToLandBitmap();
        }

        /// <summary>
        /// Updates the AABBMin and AABBMax values after area/shape modification of the land object
        /// </summary>
        private void UpdateAABBAndAreaValues()
        {
            int min_x = 64;
            int min_y = 64;
            int max_x = 0;
            int max_y = 0;
            int tempArea = 0;
            int x, y;
            for (x = 0; x < 64; x++)
            {
                for (y = 0; y < 64; y++)
                {
                    if (LandBitmap[x, y] == true)
                    {
                        if (min_x > x) min_x = x;
                        if (min_y > y) min_y = y;
                        if (max_x < x) max_x = x;
                        if (max_y < y) max_y = y;
                        tempArea += 16; //16sqm peice of land
                    }
                }
            }
            int tx = min_x * 4;
            if (tx > ((int)Constants.RegionSize - 1))
                tx = ((int)Constants.RegionSize - 1);
            int ty = min_y * 4;
            if (ty > ((int)Constants.RegionSize - 1))
                ty = ((int)Constants.RegionSize - 1);
            LandData.AABBMin =
                new Vector3((float) (min_x * 4), (float) (min_y * 4),
                              (float) m_scene.Heightmap[tx, ty]);

            tx = max_x * 4;
            if (tx > ((int)Constants.RegionSize - 1))
                tx = ((int)Constants.RegionSize - 1);
            ty = max_y * 4;
            if (ty > ((int)Constants.RegionSize - 1))
                ty = ((int)Constants.RegionSize - 1);
            LandData.AABBMax =
                new Vector3((float) (max_x * 4), (float) (max_y * 4),
                              (float) m_scene.Heightmap[tx, ty]);
            LandData.Area = tempArea;
        }

        #endregion

        #region Land Bitmap Functions

        /// <summary>
        /// Sets the land's bitmap manually
        /// </summary>
        /// <param name="bitmap">64x64 block representing where this land is on a map</param>
        public void SetLandBitmap(bool[,] bitmap)
        {
            if (bitmap.GetLength(0) != 64 || bitmap.GetLength(1) != 64 || bitmap.Rank != 2)
            {
                //Throw an exception - The bitmap is not 64x64
                //throw new Exception("Error: Invalid Parcel Bitmap");
            }
            else
            {
                //Valid: Lets set it
                LandBitmap = bitmap;
                ForceUpdateLandInfo();
            }
        }

        /// <summary>
        /// Gets the land's bitmap manually
        /// </summary>
        /// <returns></returns>
        public bool[,] GetLandBitmap()
        {
            return LandBitmap;
        }

        /// <summary>
        /// Full sim land object creation
        /// </summary>
        /// <returns></returns>
        public bool[,] BasicFullRegionLandBitmap()
        {
            return GetSquareLandBitmap(0, 0, (int) Constants.RegionSize, (int) Constants.RegionSize);
        }

        /// <summary>
        /// Used to modify the bitmap between the x and y points. Points use 64 scale
        /// </summary>
        /// <param name="start_x"></param>
        /// <param name="start_y"></param>
        /// <param name="end_x"></param>
        /// <param name="end_y"></param>
        /// <returns></returns>
        public bool[,] GetSquareLandBitmap(int start_x, int start_y, int end_x, int end_y)
        {
            bool[,] tempBitmap = new bool[64,64];
            tempBitmap.Initialize();

            tempBitmap = ModifyLandBitmapSquare(tempBitmap, start_x, start_y, end_x, end_y, true);
            return tempBitmap;
        }

        /// <summary>
        /// Change a land bitmap at within a square and set those points to a specific value
        /// </summary>
        /// <param name="land_bitmap"></param>
        /// <param name="start_x"></param>
        /// <param name="start_y"></param>
        /// <param name="end_x"></param>
        /// <param name="end_y"></param>
        /// <param name="set_value"></param>
        /// <returns></returns>
        public bool[,] ModifyLandBitmapSquare(bool[,] land_bitmap, int start_x, int start_y, int end_x, int end_y,
                                              bool set_value)
        {
            if (land_bitmap.GetLength(0) != 64 || land_bitmap.GetLength(1) != 64 || land_bitmap.Rank != 2)
            {
                //Throw an exception - The bitmap is not 64x64
                //throw new Exception("Error: Invalid Parcel Bitmap in modifyLandBitmapSquare()");
            }

            int x, y;
            for (y = 0; y < 64; y++)
            {
                for (x = 0; x < 64; x++)
                {
                    if (x >= start_x / 4 && x < end_x / 4
                        && y >= start_y / 4 && y < end_y / 4)
                    {
                        land_bitmap[x, y] = set_value;
                    }
                }
            }
            return land_bitmap;
        }

        /// <summary>
        /// Join the true values of 2 bitmaps together
        /// </summary>
        /// <param name="bitmap_base"></param>
        /// <param name="bitmap_add"></param>
        /// <returns></returns>
        public bool[,] MergeLandBitmaps(bool[,] bitmap_base, bool[,] bitmap_add)
        {
            if (bitmap_base.GetLength(0) != 64 || bitmap_base.GetLength(1) != 64 || bitmap_base.Rank != 2)
            {
                //Throw an exception - The bitmap is not 64x64
                throw new Exception("Error: Invalid Parcel Bitmap - Bitmap_base in mergeLandBitmaps");
            }
            if (bitmap_add.GetLength(0) != 64 || bitmap_add.GetLength(1) != 64 || bitmap_add.Rank != 2)
            {
                //Throw an exception - The bitmap is not 64x64
                throw new Exception("Error: Invalid Parcel Bitmap - Bitmap_add in mergeLandBitmaps");
            }

            int x, y;
            for (y = 0; y < 64; y++)
            {
                for (x = 0; x < 64; x++)
                {
                    if (bitmap_add[x, y])
                    {
                        bitmap_base[x, y] = true;
                    }
                }
            }
            return bitmap_base;
        }

        /// <summary>
        /// Converts the land bitmap to a packet friendly byte array
        /// </summary>
        /// <returns></returns>
        private byte[] ConvertLandBitmapToBytes()
        {
            byte[] tempConvertArr = new byte[512];
            byte tempByte = 0;
            int x, y, i, byteNum = 0;
            i = 0;
            for (y = 0; y < 64; y++)
            {
                for (x = 0; x < 64; x++)
                {
                    tempByte = Convert.ToByte(tempByte | Convert.ToByte(LandBitmap[x, y]) << (i++ % 8));
                    if (i % 8 == 0)
                    {
                        tempConvertArr[byteNum] = tempByte;
                        tempByte = (byte) 0;
                        i = 0;
                        byteNum++;
                    }
                }
            }
            return tempConvertArr;
        }

        private bool[,] ConvertBytesToLandBitmap()
        {
            bool[,] tempConvertMap = new bool[landArrayMax, landArrayMax];
            tempConvertMap.Initialize();
            byte tempByte = 0;
            int x = 0, y = 0, i = 0, bitNum = 0;
            for (i = 0; i < 512; i++)
            {
                tempByte = LandData.Bitmap[i];
                for (bitNum = 0; bitNum < 8; bitNum++)
                {
                    bool bit = Convert.ToBoolean(Convert.ToByte(tempByte >> bitNum) & (byte) 1);
                    tempConvertMap[x, y] = bit;
                    x++;
                    if (x > 63)
                    {
                        x = 0;
                        y++;
                    }
                }
            }
            return tempConvertMap;
        }

        #endregion

        #region Object Select and Object Owner Listing

        public void SendForceObjectSelect(int local_id, int request_type, List<UUID> returnIDs, IClientAPI remote_client)
        {
            if (m_scene.Permissions.CanEditParcel(remote_client.AgentId, this))
            {
                List<uint> resultLocalIDs = new List<uint>();
                try
                {
                    lock (primsOverMe)
                    {
                        foreach (SceneObjectGroup obj in primsOverMe)
                        {
                            if (obj.LocalId > 0)
                            {
                                if (request_type == LandChannel.LAND_SELECT_OBJECTS_OWNER && obj.OwnerID == LandData.OwnerID)
                                {
                                    resultLocalIDs.Add(obj.LocalId);
                                }
                                else if (request_type == LandChannel.LAND_SELECT_OBJECTS_GROUP && obj.GroupID == LandData.GroupID && LandData.GroupID != UUID.Zero)
                                {
                                    resultLocalIDs.Add(obj.LocalId);
                                }
                                else if (request_type == LandChannel.LAND_SELECT_OBJECTS_OTHER &&
                                         obj.OwnerID != remote_client.AgentId)
                                {
                                    resultLocalIDs.Add(obj.LocalId);
                                }
                                else if (request_type == (int)ObjectReturnType.List && returnIDs.Contains(obj.OwnerID))
                                {
                                    resultLocalIDs.Add(obj.LocalId);
                                }
                            }
                        }
                    }
                } catch (InvalidOperationException)
                {
                    m_log.Error("[LAND]: Unable to force select the parcel objects. Arr.");
                }

                remote_client.SendForceClientSelectObjects(resultLocalIDs);
            }
        }

        /// <summary>
        /// Notify the parcel owner each avatar that owns prims situated on their land.  This notification includes
        /// aggreagete details such as the number of prims.
        ///
        /// </summary>
        /// <param name="remote_client">
        /// <see cref="IClientAPI"/>
        /// </param>
        public void SendLandObjectOwners(IClientAPI remote_client)
        {
            if (m_scene.Permissions.CanViewObjectOwners(remote_client.AgentId, this))
            {
                Dictionary<UUID, int> primCount = new Dictionary<UUID, int>();
                List<UUID> groups = new List<UUID>();

                lock (primsOverMe)
                {
                    try
                    {

                        foreach (SceneObjectGroup obj in primsOverMe)
                        {
                            try
                            {
                                if (!primCount.ContainsKey(obj.OwnerID))
                                {
                                    primCount.Add(obj.OwnerID, 0);
                                }
                            }
                            catch (NullReferenceException)
                            {
                                m_log.Info("[LAND]: " + "Got Null Reference when searching land owners from the parcel panel");
                            }
                            try
                            {
                                primCount[obj.OwnerID] += obj.PrimCount;
                            }
                            catch (KeyNotFoundException)
                            {
                                m_log.Error("[LAND]: Unable to match a prim with it's owner.");
                            }
                            if (obj.OwnerID == obj.GroupID && (!groups.Contains(obj.OwnerID)))
                                groups.Add(obj.OwnerID);
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        m_log.Error("[LAND]: Unable to Enumerate Land object arr.");
                    }
                }

                remote_client.SendLandObjectOwners(LandData, groups, primCount);
            }
        }

        public Dictionary<UUID, int> GetLandObjectOwners()
        {
            Dictionary<UUID, int> ownersAndCount = new Dictionary<UUID, int>();
            lock (primsOverMe)
            {
                try
                {

                    foreach (SceneObjectGroup obj in primsOverMe)
                    {
                        if (!ownersAndCount.ContainsKey(obj.OwnerID))
                        {
                            ownersAndCount.Add(obj.OwnerID, 0);
                        }
                        ownersAndCount[obj.OwnerID] += obj.PrimCount;
                    }
                }
                catch (InvalidOperationException)
                {
                    m_log.Error("[LAND]: Unable to enumerate land owners. arr.");
                }

            }
            return ownersAndCount;
        }

        #endregion

        #region Object Returning

        public void ReturnObject(SceneObjectGroup obj)
        {
            SceneObjectGroup[] objs = new SceneObjectGroup[1];
            objs[0] = obj;
            m_scene.returnObjects(objs, obj.OwnerID);
        }

        public List<SceneObjectGroup> GetPrimsOverByOwner(UUID targetID, int flags)
        {
            List<SceneObjectGroup> prims = new List<SceneObjectGroup>();
            lock (primsOverMe)
            {
                foreach (SceneObjectGroup obj in primsOverMe)
                {
                    if (obj.OwnerID == m_landData.OwnerID)
                    {
                        if (flags == 4 && //Scripted
                            (obj.RootPart.Flags & PrimFlags.Scripted) == PrimFlags.Scripted)
                            continue;
                        prims.Add(obj);
                    }
                }
            }
            return prims;
        }

        public void ReturnLandObjects(uint type, UUID[] owners, UUID[] tasks, IClientAPI remote_client)
        {
            Dictionary<UUID,List<SceneObjectGroup>> returns =
                    new Dictionary<UUID,List<SceneObjectGroup>>();

            lock (primsOverMe)
            {
                if (type == (uint)ObjectReturnType.Owner)
                {
                    foreach (SceneObjectGroup obj in primsOverMe)
                    {
                        if (obj.OwnerID == m_landData.OwnerID)
                        {
                            if (!returns.ContainsKey(obj.OwnerID))
                                returns[obj.OwnerID] =
                                        new List<SceneObjectGroup>();
                            returns[obj.OwnerID].Add(obj);
                        }
                    }
                }
                else if (type == (uint)ObjectReturnType.Group && m_landData.GroupID != UUID.Zero)
                {
                    foreach (SceneObjectGroup obj in primsOverMe)
                    {
                        if (obj.GroupID == m_landData.GroupID)
                        {
                            if (!returns.ContainsKey(obj.OwnerID))
                                returns[obj.OwnerID] =
                                        new List<SceneObjectGroup>();
                            returns[obj.OwnerID].Add(obj);
                        }
                    }
                }
                else if (type == (uint)ObjectReturnType.Other)
                {
                    foreach (SceneObjectGroup obj in primsOverMe)
                    {
                        if (obj.OwnerID != m_landData.OwnerID &&
                            (obj.GroupID != m_landData.GroupID ||
                            m_landData.GroupID == UUID.Zero))
                        {
                            if (!returns.ContainsKey(obj.OwnerID))
                                returns[obj.OwnerID] =
                                        new List<SceneObjectGroup>();
                            returns[obj.OwnerID].Add(obj);
                        }
                    }
                }
                else if (type == (uint)ObjectReturnType.List)
                {
                    List<UUID> ownerlist = new List<UUID>(owners);

                    foreach (SceneObjectGroup obj in primsOverMe)
                    {
                        if (ownerlist.Contains(obj.OwnerID))
                        {
                            if (!returns.ContainsKey(obj.OwnerID))
                                returns[obj.OwnerID] =
                                        new List<SceneObjectGroup>();
                            returns[obj.OwnerID].Add(obj);
                        }
                    }
                }
                else if (type == 1)
                {
                    List<UUID> Tasks = new List<UUID>(tasks);
                    foreach (SceneObjectGroup obj in primsOverMe)
                    {
                        if (Tasks.Contains(obj.UUID))
                        {
                            if (!returns.ContainsKey(obj.OwnerID))
                                returns[obj.OwnerID] =
                                        new List<SceneObjectGroup>();
                            returns[obj.OwnerID].Add(obj);
                        }
                    }
                }
            }

            foreach (List<SceneObjectGroup> ol in returns.Values)
            {
                if (m_scene.Permissions.CanReturnObjects(this, remote_client.AgentId, ol))
                {
                    //The return system will take care of the returned objects
                    m_scene.AddReturns(ol[0].OwnerID, ol[0].Name, ol.Count, ol[0].AbsolutePosition, "parcel owner return", ol);
                    //m_scene.returnObjects(ol.ToArray(), remote_client.AgentId);
                }
            }
        }

        public void DisableLandObjects(uint type, UUID[] owners, UUID[] tasks, IClientAPI remote_client)
        {
            Dictionary<UUID, List<SceneObjectGroup>> disabled =
                    new Dictionary<UUID, List<SceneObjectGroup>>();

            lock (primsOverMe)
            {
                if (type == (uint)ObjectReturnType.Owner)
                {
                    foreach (SceneObjectGroup obj in primsOverMe)
                    {
                        if (obj.OwnerID == m_landData.OwnerID)
                        {
                            if (!disabled.ContainsKey(obj.OwnerID))
                                disabled[obj.OwnerID] =
                                        new List<SceneObjectGroup>();
                            disabled[obj.OwnerID].Add(obj);
                        }
                    }
                }
                else if (type == (uint)ObjectReturnType.Group && m_landData.GroupID != UUID.Zero)
                {
                    foreach (SceneObjectGroup obj in primsOverMe)
                    {
                        if (obj.GroupID == m_landData.GroupID)
                        {
                            if (!disabled.ContainsKey(obj.OwnerID))
                                disabled[obj.OwnerID] =
                                        new List<SceneObjectGroup>();
                            disabled[obj.OwnerID].Add(obj);
                        }
                    }
                }
                else if (type == (uint)ObjectReturnType.Other)
                {
                    foreach (SceneObjectGroup obj in primsOverMe)
                    {
                        if (obj.OwnerID != m_landData.OwnerID &&
                            (obj.GroupID != m_landData.GroupID ||
                            m_landData.GroupID == UUID.Zero))
                        {
                            if (!disabled.ContainsKey(obj.OwnerID))
                                disabled[obj.OwnerID] =
                                        new List<SceneObjectGroup>();
                            disabled[obj.OwnerID].Add(obj);
                        }
                    }
                }
                else if (type == (uint)ObjectReturnType.List)
                {
                    List<UUID> ownerlist = new List<UUID>(owners);

                    foreach (SceneObjectGroup obj in primsOverMe)
                    {
                        if (ownerlist.Contains(obj.OwnerID))
                        {
                            if (!disabled.ContainsKey(obj.OwnerID))
                                disabled[obj.OwnerID] =
                                        new List<SceneObjectGroup>();
                            disabled[obj.OwnerID].Add(obj);
                        }
                    }
                }
                else if (type == 1)
                {
                    List<UUID> Tasks = new List<UUID>(tasks);
                    foreach (SceneObjectGroup obj in primsOverMe)
                    {
                        if (Tasks.Contains(obj.UUID))
                        {
                            if (!disabled.ContainsKey(obj.OwnerID))
                                disabled[obj.OwnerID] =
                                        new List<SceneObjectGroup>();
                            disabled[obj.OwnerID].Add(obj);
                        }
                    }
                }
            }

            IScriptModule[] modules = m_scene.RequestModuleInterfaces<IScriptModule>();
            foreach (List<SceneObjectGroup> ol in disabled.Values)
            {
                foreach (SceneObjectGroup group in ol)
                {
                    if (m_scene.Permissions.CanEditObject(group.UUID, remote_client.AgentId))
                    {
                        foreach (IScriptModule module in modules)
                        {
                            //Disable the entire object
                            foreach (SceneObjectPart part in group.ChildrenList)
                            {
                                foreach (TaskInventoryItem item in part.Inventory.GetInventoryItems())
                                {
                                    if (item.InvType == (int)InventoryType.LSL)
                                    {
                                        module.SuspendScript(item.ItemID);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        #endregion

        #region Object Adding/Removing from Parcel

        public void ResetLandPrimCounts()
        {
            LandData.GroupPrims = 0;
            LandData.OwnerPrims = 0;
            LandData.OtherPrims = 0;
            LandData.SelectedPrims = 0;


            lock (primsOverMe)
                primsOverMe.Clear();
        }

        public void AddPrimToCount(SceneObjectGroup obj)
        {

            UUID prim_owner = obj.OwnerID;
            int prim_count = obj.PrimCount;

            if (obj.IsSelected)
                LandData.SelectedPrims += prim_count;
            else
            {
                if (prim_owner == LandData.OwnerID)
                    LandData.OwnerPrims += prim_count;
                else if ((obj.GroupID == LandData.GroupID ||
                          prim_owner  == LandData.GroupID) &&
                          LandData.GroupID != UUID.Zero)
                    LandData.GroupPrims += prim_count;
                else
                    LandData.OtherPrims += prim_count;
            }

            lock (primsOverMe)
                primsOverMe.Add(obj);
        }

        public void RemovePrimFromCount(SceneObjectGroup obj)
        {
            lock (primsOverMe)
            {
                if (primsOverMe.Contains(obj))
                {
                    UUID prim_owner = obj.OwnerID;
                    int prim_count = obj.PrimCount;

                    if (prim_owner == LandData.OwnerID)
                    {
                        LandData.OwnerPrims -= prim_count;
                    }
                    else if (obj.GroupID == LandData.GroupID ||
                             prim_owner  == LandData.GroupID)
                    {
                        LandData.GroupPrims -= prim_count;
                    }
                    else
                    {
                        LandData.OtherPrims -= prim_count;
                    }

                    primsOverMe.Remove(obj);
                }
            }
        }

        #endregion

        #endregion

        #endregion
        
        /// <summary>
        /// Set the media url for this land parcel
        /// </summary>
        /// <param name="url"></param>
        public void SetMediaUrl(string url)
        {
            LandData.MediaURL = url;
            SendLandUpdateToAvatarsOverMe();
        }
        
        /// <summary>
        /// Set the music url for this land parcel
        /// </summary>
        /// <param name="url"></param>
        public void SetMusicUrl(string url)
        {
            LandData.MusicURL = url;
            SendLandUpdateToAvatarsOverMe();
        }
    }
}
