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
using System.Collections;
using System.Reflection;
using System.Text;
using System.Timers;
using OpenMetaverse;
using OpenMetaverse.Packets;
using log4net;
using OpenSim.Framework;
using OpenSim.Region.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes.Serialization;

namespace OpenSim.Region.Framework.Scenes
{
    public partial class Scene
    {
        private static readonly ILog m_log
            = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Allows asynchronous derezzing of objects from the scene into a client's inventory.
        /// </summary>
        protected AsyncSceneObjectGroupDeleter m_asyncSceneObjectDeleter;

        /// <summary>
        /// Start all the scripts in the scene which should be started.
        /// </summary>
        public void CreateScriptInstances()
        {
            m_log.Info("[PRIM INVENTORY]: Starting scripts in scene");
            //Set loading prims here to block backup
            LoadingPrims = true;
            EntityBase[] entities = Entities.GetEntities();
            foreach (EntityBase group in entities)
            {
                if (group is SceneObjectGroup)
                {
                    ((SceneObjectGroup)group).CreateScriptInstances(0, false, DefaultScriptEngine, 0, UUID.Zero);
                    ((SceneObjectGroup)group).ResumeScripts();
                }
            }
            //Now reset it
            LoadingPrims = false;
        }

        public void AddUploadedInventoryItem(UUID agentID, InventoryItemBase item)
        {
            IMoneyModule money = RequestModuleInterface<IMoneyModule>();
            if (money != null)
            {
                if (!money.AmountCovered(GetScenePresence(agentID).ControllingClient, money.UploadCharge))
                {
                    GetScenePresence(agentID).ControllingClient.SendAlertMessage("You do not have enough money to complete this upload.");
                    return;
                }
                money.ApplyUploadCharge(agentID, money.UploadCharge, "Asset upload");
            }

            AddInventoryItem(item);
        }

        public bool AddInventoryItemReturned(UUID AgentId, InventoryItemBase item)
        {
            if (AddInventoryItem(item))
                return true;
            else
            {
                m_log.WarnFormat(
                    "[AGENT INVENTORY]: Unable to add item {1} to agent {2} inventory", item.Name, AgentId);

                return false;
            }
        }

        /// <summary>
        /// Add the given inventory item to a user's inventory.
        /// </summary>
        /// <param name="item"></param>
        public bool AddInventoryItem(InventoryItemBase item)
        {
            if (UUID.Zero == item.Folder)
            {
                InventoryFolderBase f = InventoryService.GetFolderForType(item.Owner, (AssetType)item.AssetType);
                if (f != null)
                {
//                    m_log.DebugFormat(
//                        "[LOCAL INVENTORY SERVICES CONNECTOR]: Found folder {0} type {1} for item {2}", 
//                        f.Name, (AssetType)f.Type, item.Name);
                    
                    item.Folder = f.ID;
                }
                else
                {
                    f = InventoryService.GetRootFolder(item.Owner);
                    if (f != null)
                    {
                        item.Folder = f.ID;
                    }
                    else
                    {
                        m_log.WarnFormat(
                            "[AGENT INVENTORY]: Could not find root folder for {0} when trying to add item {1} with no parent folder specified",
                            item.Owner, item.Name);
                        return false;
                    }
                }
            }
            
            if (InventoryService.AddItem(item))
            {
                int userlevel = 0;
                if (Permissions.IsGod(item.Owner))
                {
                    userlevel = 1;
                }
                EventManager.TriggerOnNewInventoryItemUploadComplete(item.Owner, item.AssetID, item.Name, userlevel);
                
                return true;
            }
            else
            {
                m_log.WarnFormat(
                    "[AGENT INVENTORY]: Agent {0} could not add item {1} {2}",
                    item.Owner, item.Name, item.ID);

                return false;
            }
        }
        
        /// <summary>
        /// Add the given inventory item to a user's inventory.
        /// </summary>
        /// <param name="AgentID">
        /// A <see cref="UUID"/>
        /// </param>
        /// <param name="item">
        /// A <see cref="InventoryItemBase"/>
        /// </param>
        [Obsolete("Use AddInventoryItem(InventoryItemBase item) instead.  This was deprecated in OpenSim 0.7.1")]
        public void AddInventoryItem(UUID AgentID, InventoryItemBase item)
        {
            AddInventoryItem(item);
        }

        /// <summary>
        /// Add an inventory item to an avatar's inventory.
        /// </summary>
        /// <param name="remoteClient">The remote client controlling the avatar</param>
        /// <param name="item">The item.  This structure contains all the item metadata, including the folder
        /// in which the item is to be placed.</param>
        public void AddInventoryItem(IClientAPI remoteClient, InventoryItemBase item)
        {
            AddInventoryItem(item);
            remoteClient.SendInventoryItemCreateUpdate(item, 0);
        }

        /// <summary>
        /// <see>CapsUpdatedInventoryItemAsset(IClientAPI, UUID, byte[])</see>
        /// </summary>
        public string CapsUpdateInventoryItemAsset(UUID avatarId, UUID itemID, byte[] data)
        {
            ScenePresence avatar;

            if (TryGetScenePresence(avatarId, out avatar))
            {
                IInventoryAccessModule invAccess = RequestModuleInterface<IInventoryAccessModule>();
                if (invAccess != null)
                    return invAccess.CapsUpdateInventoryItemAsset(avatar.ControllingClient, itemID, data);
            }
            else
            {
                m_log.ErrorFormat(
                    "[AGENT INVENTORY]: " +
                    "Avatar {0} cannot be found to update its inventory item asset",
                    avatarId);
            }

            return "";
        }

        /// <summary>
        /// Capability originating call to update the asset of a script in a prim's (task's) inventory
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="itemID"></param>
        /// <param name="primID">The prim which contains the item to update</param>
        /// <param name="isScriptRunning">Indicates whether the script to update is currently running</param>
        /// <param name="data"></param>
        public ArrayList CapsUpdateTaskInventoryScriptAsset(IClientAPI remoteClient, UUID itemId,
                                                       UUID primId, bool isScriptRunning, byte[] data)
        {
            if (!Permissions.CanEditScript(itemId, primId, remoteClient.AgentId))
            {
                remoteClient.SendAgentAlertMessage("Insufficient permissions to edit script", false);
                return new ArrayList();
            }

            // Retrieve group
            SceneObjectPart part = GetSceneObjectPart(primId);
            if (null == part.ParentGroup)
            {
                m_log.ErrorFormat(
                    "[PRIM INVENTORY]: " +
                    "Prim inventory update requested for item ID {0} in prim ID {1} but this prim does not exist",
                    itemId, primId);

                return new ArrayList();
            }
            
            // Retrieve item
            TaskInventoryItem item = part.Inventory.GetInventoryItem(itemId);

            if (null == item)
            {
                m_log.ErrorFormat(
                    "[PRIM INVENTORY]: Tried to retrieve item ID {0} from prim {1}, {2} for caps script update "
                        + " but the item does not exist in this inventory",
                    itemId, part.Name, part.UUID);

                return new ArrayList();
            }

            AssetBase asset = CreateAsset(item.Name, item.Description, (sbyte)AssetType.LSLText, data, remoteClient.AgentId);
            AssetService.Store(asset);

            // Update item with new asset
            item.AssetID = asset.FullID;

            if (part.ParentGroup.UpdateInventoryItem(item))
                if(item.InvType == (int)InventoryType.LSL)
                    remoteClient.SendAgentAlertMessage("Script saved", false);                        
            
            // Trigger rerunning of script (use TriggerRezScript event, see RezScript)
            ArrayList errors = new ArrayList();

            if (isScriptRunning)
            {
                // Needs to determine which engine was running it and use that
                //
                part.Inventory.UpdateScriptInstance(item.ItemID, 0, false, DefaultScriptEngine, 0);
                errors = part.Inventory.GetScriptErrors(item.ItemID, DefaultScriptEngine);
            }
            else
            {
                remoteClient.SendAgentAlertMessage("Script saved", false);
            }
            part.GetProperties(remoteClient);

            part.ParentGroup.ResumeScripts();
            return errors;
        }

        /// <summary>
        /// <see>CapsUpdateTaskInventoryScriptAsset(IClientAPI, UUID, UUID, bool, byte[])</see>
        /// </summary>
        public ArrayList CapsUpdateTaskInventoryScriptAsset(UUID avatarId, UUID itemId,
                                                        UUID primId, bool isScriptRunning, byte[] data)
        {
            IClientAPI avatar;

            if (TryGetClient(avatarId, out avatar))
            {
                return CapsUpdateTaskInventoryScriptAsset(
                    avatar, itemId, primId, isScriptRunning, data);
            }
            else
            {
                m_log.ErrorFormat(
                    "[PRIM INVENTORY]: " +
                    "Avatar {0} cannot be found to update its prim item asset",
                    avatarId);
                return new ArrayList();
            }
        }

        /// <summary>
        /// Update an item which is either already in the client's inventory or is within
        /// a transaction
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="transactionID">The transaction ID.  If this is UUID.Zero we will
        /// assume that we are not in a transaction</param>
        /// <param name="itemID">The ID of the updated item</param>
        /// <param name="name">The name of the updated item</param>
        /// <param name="description">The description of the updated item</param>
        /// <param name="nextOwnerMask">The permissions of the updated item</param>
        public void UpdateInventoryItemAsset(IClientAPI remoteClient, UUID transactionID,
                                             UUID itemID, InventoryItemBase itemUpd)
        {
            // This one will let people set next perms on items in agent
            // inventory. Rut-Roh. Whatever. Make this secure. Yeah.
            //
            // Passing something to another avatar or a an object will already
            InventoryItemBase item = new InventoryItemBase(itemID, remoteClient.AgentId);
            item = InventoryService.GetItem(item);

            if (item != null)
            {
                if (UUID.Zero == transactionID)
                {
                    item.Name = itemUpd.Name;
                    item.Description = itemUpd.Description;
                    item.NextPermissions = itemUpd.NextPermissions & item.BasePermissions;
                    item.EveryOnePermissions = itemUpd.EveryOnePermissions & item.BasePermissions;
                    item.GroupPermissions = itemUpd.GroupPermissions & item.BasePermissions;
                    item.GroupID = itemUpd.GroupID;
                    item.GroupOwned = itemUpd.GroupOwned;
                    item.CreationDate = itemUpd.CreationDate;
                    // The client sends zero if its newly created?

                    if (itemUpd.CreationDate == 0)
                        item.CreationDate = Util.UnixTimeSinceEpoch();
                    else
                        item.CreationDate = itemUpd.CreationDate;

                    // TODO: Check if folder changed and move item
                    //item.NextPermissions = itemUpd.Folder;
                    item.InvType = itemUpd.InvType;
                    item.SalePrice = itemUpd.SalePrice;
                    item.SaleType = itemUpd.SaleType;
                    item.Flags = itemUpd.Flags;

                    InventoryService.UpdateItem(item);
                }
                else
                {
                    IAgentAssetTransactions agentTransactions = this.RequestModuleInterface<IAgentAssetTransactions>();
                    if (agentTransactions != null)
                    {
                        agentTransactions.HandleItemUpdateFromTransaction(
                                     remoteClient, transactionID, item);
                    }
                }
            }
            else
            {
                m_log.Error(
                    "[AGENTINVENTORY]: Item ID " + itemID + " not found for an inventory item update.");
            }
        }

        public void ChangeInventoryItemFlags(IClientAPI remoteClient, UUID itemID, uint Flags)
        {
            InventoryItemBase item = new InventoryItemBase(itemID, remoteClient.AgentId);
            item = InventoryService.GetItem(item);

            if (item != null)
            {
                item.Flags = Flags;

                InventoryService.UpdateItem(item);
                remoteClient.SendInventoryItemDetails(item.Owner, item);
            }
            else
            {
                m_log.Error(
                    "[AGENTINVENTORY]: Item ID " + itemID + " not found for an inventory item update.");
            }
        }

        /// <summary>
        /// Give an inventory item from one user to another
        /// </summary>
        /// <param name="recipientClient"></param>
        /// <param name="senderId">ID of the sender of the item</param>
        /// <param name="itemId"></param>
        public virtual void GiveInventoryItem(IClientAPI recipientClient, UUID senderId, UUID itemId)
        {
            InventoryItemBase itemCopy = GiveInventoryItem(recipientClient.AgentId, senderId, itemId);

            if (itemCopy != null)
                recipientClient.SendBulkUpdateInventory(itemCopy);
        }

        /// <summary>
        /// Give an inventory item from one user to another
        /// </summary>
        /// <param name="recipient"></param>
        /// <param name="senderId">ID of the sender of the item</param>
        /// <param name="itemId"></param>
        /// <returns>The inventory item copy given, null if the give was unsuccessful</returns>
        public virtual InventoryItemBase GiveInventoryItem(UUID recipient, UUID senderId, UUID itemId)
        {
            return GiveInventoryItem(recipient, senderId, itemId, UUID.Zero);
        }

        /// <summary>
        /// Give an inventory item from one user to another
        /// </summary>
        /// <param name="recipient"></param>
        /// <param name="senderId">ID of the sender of the item</param>
        /// <param name="itemId"></param>
        /// <param name="recipientFolderId">
        /// The id of the folder in which the copy item should go.  If UUID.Zero then the item is placed in the most
        /// appropriate default folder.
        /// </param>
        /// <returns>
        /// The inventory item copy given, null if the give was unsuccessful
        /// </returns>
        public virtual InventoryItemBase GiveInventoryItem(
            UUID recipient, UUID senderId, UUID itemId, UUID recipientFolderId)
        {
            //Console.WriteLine("Scene.Inventory.cs: GiveInventoryItem");

            InventoryItemBase item = new InventoryItemBase(itemId, senderId);
            item = InventoryService.GetItem(item);

            if ((item != null) && (item.Owner == senderId))
            {
                if (!Permissions.BypassPermissions())
                {
                    if ((item.CurrentPermissions & (uint)PermissionMask.Transfer) == 0)
                        return null;
                }

                // Insert a copy of the item into the recipient
                InventoryItemBase itemCopy = new InventoryItemBase();
                itemCopy.Owner = recipient;
                itemCopy.CreatorId = item.CreatorId;
                itemCopy.ID = UUID.Random();
                itemCopy.AssetID = item.AssetID;
                itemCopy.Description = item.Description;
                itemCopy.Name = item.Name;
                itemCopy.AssetType = item.AssetType;
                itemCopy.InvType = item.InvType;
                itemCopy.Folder = recipientFolderId;

                if (Permissions.PropagatePermissions() && recipient != senderId)
                {
                    // Trying to do this right this time. This is evil. If
                    // you believe in Good, go elsewhere. Vampires and other
                    // evil creatores only beyond this point. You have been
                    // warned.

                    // We're going to mask a lot of things by the next perms
                    // Tweak the next perms to be nicer to our data
                    //
                    // In this mask, all the bits we do NOT want to mess
                    // with are set. These are:
                    //
                    // Transfer
                    // Copy
                    // Modufy
                    uint permsMask = ~ ((uint)PermissionMask.Copy |
                                        (uint)PermissionMask.Transfer |
                                        (uint)PermissionMask.Modify);

                    // Now, reduce the next perms to the mask bits
                    // relevant to the operation
                    uint nextPerms = permsMask | (item.NextPermissions &
                                      ((uint)PermissionMask.Copy |
                                       (uint)PermissionMask.Transfer |
                                       (uint)PermissionMask.Modify));

                    // nextPerms now has all bits set, except for the actual
                    // next permission bits.

                    // This checks for no mod, no copy, no trans.
                    // This indicates an error or messed up item. Do it like
                    // SL and assume trans
                    if (nextPerms == permsMask)
                        nextPerms |= (uint)PermissionMask.Transfer;

                    // Inventory owner perms are the logical AND of the
                    // folded perms and the root prim perms, however, if
                    // the root prim is mod, the inventory perms will be
                    // mod. This happens on "take" and is of little concern
                    // here, save for preventing escalation

                    // This hack ensures that items previously permalocked
                    // get unlocked when they're passed or rezzed
                    uint basePerms = item.BasePermissions |
                                    (uint)PermissionMask.Move;
                    uint ownerPerms = item.CurrentPermissions;

                    // If this is an object, root prim perms may be more
                    // permissive than folded perms. Use folded perms as
                    // a mask
                    if (item.InvType == (int)InventoryType.Object)
                    {
                        // Create a safe mask for the current perms
                        uint foldedPerms = (item.CurrentPermissions & 7) << 13;
                        foldedPerms |= permsMask;

                        bool isRootMod = (item.CurrentPermissions &
                                          (uint)PermissionMask.Modify) != 0 ?
                                          true : false;

                        // Mask the owner perms to the folded perms
                        ownerPerms &= foldedPerms;
                        basePerms &= foldedPerms;

                        // If the root was mod, let the mask reflect that
                        // We also need to adjust the base here, because
                        // we should be able to edit in-inventory perms
                        // for the root prim, if it's mod.
                        if (isRootMod)
                        {
                            ownerPerms |= (uint)PermissionMask.Modify;
                            basePerms |= (uint)PermissionMask.Modify;
                        }
                    }

                    // These will be applied to the root prim at next rez.
                    // The slam bit (bit 3) and folded permission (bits 0-2)
                    // are preserved due to the above mangling
                    ownerPerms &= nextPerms;

                    // Mask the base permissions. This is a conservative
                    // approach altering only the three main perms
                    basePerms &= nextPerms;

                    // Assign to the actual item. Make sure the slam bit is
                    // set, if it wasn't set before.
                    itemCopy.BasePermissions = basePerms;
                    itemCopy.CurrentPermissions = ownerPerms | 16; // Slam

                    itemCopy.NextPermissions = item.NextPermissions;

                    // This preserves "everyone can move"
                    itemCopy.EveryOnePermissions = item.EveryOnePermissions &
                                                   nextPerms;

                    // Intentionally killing "share with group" here, as
                    // the recipient will not have the group this is
                    // set to
                    itemCopy.GroupPermissions = 0;
                }
                else
                {
                    itemCopy.CurrentPermissions = item.CurrentPermissions;
                    itemCopy.NextPermissions = item.NextPermissions;
                    itemCopy.EveryOnePermissions = item.EveryOnePermissions & item.NextPermissions;
                    itemCopy.GroupPermissions = item.GroupPermissions & item.NextPermissions;
                    itemCopy.BasePermissions = item.BasePermissions;
                }
                
                if (itemCopy.Folder == UUID.Zero)
                {
                    InventoryFolderBase folder = InventoryService.GetFolderForType(recipient, (AssetType)itemCopy.AssetType);

                    if (folder != null)
                    {
                        itemCopy.Folder = folder.ID;
                    }
                    else
                    {
                        InventoryFolderBase root = InventoryService.GetRootFolder(recipient);

                        if (root != null)
                            itemCopy.Folder = root.ID;
                        else
                            return null; // No destination
                    }
                }

                itemCopy.GroupID = UUID.Zero;
                itemCopy.GroupOwned = false;
                itemCopy.Flags = item.Flags;
                itemCopy.SalePrice = item.SalePrice;
                itemCopy.SaleType = item.SaleType;

                if (AddInventoryItem(itemCopy))
                {
                    IInventoryAccessModule invAccess = RequestModuleInterface<IInventoryAccessModule>();
                    if (invAccess != null)
                        invAccess.TransferInventoryAssets(itemCopy, senderId, recipient);
                }

                if (!Permissions.BypassPermissions())
                {
                    if ((item.CurrentPermissions & (uint)PermissionMask.Copy) == 0)
                    {
                        List<UUID> items = new List<UUID>();
                        items.Add(itemId);
                        InventoryService.DeleteItems(senderId, items);
                    }
                }

                return itemCopy;
            }
            else
            {
                m_log.WarnFormat("[AGENT INVENTORY]: Failed to find item {0} or item does not belong to giver ", itemId);
                return null;
            }

        }

        /// <summary>
        /// Give an entire inventory folder from one user to another.  The entire contents (including all descendent
        /// folders) is given.
        /// </summary>
        /// <param name="recipientId"></param>
        /// <param name="senderId">ID of the sender of the item</param>
        /// <param name="folderId"></param>
        /// <param name="recipientParentFolderId">
        /// The id of the receipient folder in which the send folder should be placed.  If UUID.Zero then the
        /// recipient folder is the root folder
        /// </param>
        /// <returns>
        /// The inventory folder copy given, null if the copy was unsuccessful
        /// </returns>
        public virtual InventoryFolderBase GiveInventoryFolder(
            UUID recipientId, UUID senderId, UUID folderId, UUID recipientParentFolderId)
        {
            //// Retrieve the folder from the sender
            InventoryFolderBase folder = InventoryService.GetFolder(new InventoryFolderBase(folderId));
            if (null == folder)
            {
                m_log.ErrorFormat(
                     "[AGENT INVENTORY]: Could not find inventory folder {0} to give", folderId);

                return null;
            }

            if (recipientParentFolderId == UUID.Zero)
            {
                InventoryFolderBase recipientRootFolder = InventoryService.GetRootFolder(recipientId);
                if (recipientRootFolder != null)
                    recipientParentFolderId = recipientRootFolder.ID;
                else
                {
                    m_log.WarnFormat("[AGENT INVENTORY]: Unable to find root folder for receiving agent");
                    return null;
                }
            }

            UUID newFolderId = UUID.Random();
            InventoryFolderBase newFolder 
                = new InventoryFolderBase(
                    newFolderId, folder.Name, recipientId, folder.Type, recipientParentFolderId, folder.Version);
            InventoryService.AddFolder(newFolder);

            // Give all the subfolders
            InventoryCollection contents = InventoryService.GetFolderContent(senderId, folderId);
            foreach (InventoryFolderBase childFolder in contents.Folders)
            {
                GiveInventoryFolder(recipientId, senderId, childFolder.ID, newFolder.ID);
            }

            // Give all the items
            foreach (InventoryItemBase item in contents.Items)
            {
                GiveInventoryItem(recipientId, senderId, item.ID, newFolder.ID);
            }

            return newFolder;
        }

        public void CopyInventoryItem(IClientAPI remoteClient, uint callbackID, UUID oldAgentID, UUID oldItemID,
                                      UUID newFolderID, string newName)
        {
            m_log.DebugFormat(
                "[AGENT INVENTORY]: CopyInventoryItem received by {0} with oldAgentID {1}, oldItemID {2}, new FolderID {3}, newName {4}",
                remoteClient.AgentId, oldAgentID, oldItemID, newFolderID, newName);

            InventoryItemBase item = null;
            if (LibraryService != null && LibraryService.LibraryRootFolder != null)
                item = LibraryService.LibraryRootFolder.FindItem(oldItemID);

            if (item == null)
            {
                item = new InventoryItemBase(oldItemID, remoteClient.AgentId);
                item = InventoryService.GetItem(item);

                if (item == null)
                {
                    m_log.Error("[AGENT INVENTORY]: Failed to find item " + oldItemID.ToString());
                    return;
                }

                if ((item.CurrentPermissions & (uint)PermissionMask.Copy) == 0)
                    return;
            }

            AssetBase asset = AssetService.Get(item.AssetID.ToString());

            if (asset != null)
            {
                if (newName != String.Empty)
                {
                    asset.Name = newName;
                }
                else
                {
                    newName = item.Name;
                }

                if (remoteClient.AgentId == oldAgentID)
                {
                    CreateNewInventoryItem(
                        remoteClient, item.CreatorId, newFolderID, newName, item.Flags, callbackID, asset, (sbyte)item.InvType,
                        item.BasePermissions, item.CurrentPermissions, item.EveryOnePermissions, item.NextPermissions, item.GroupPermissions, Util.UnixTimeSinceEpoch());
                }
                else
                {
                    CreateNewInventoryItem(
                        remoteClient, item.CreatorId, newFolderID, newName, item.Flags, callbackID, asset, (sbyte)item.InvType,
                        item.NextPermissions, item.NextPermissions, item.EveryOnePermissions & item.NextPermissions, item.NextPermissions, item.GroupPermissions, Util.UnixTimeSinceEpoch());
                }
            }
            else
            {
                m_log.ErrorFormat(
                    "[AGENT INVENTORY]: Could not copy item {0} since asset {1} could not be found",
                    item.Name, item.AssetID);
            }
        }

        /// <summary>
        /// Create a new asset data structure.
        /// </summary>
        public AssetBase CreateAsset(string name, string description, sbyte assetType, byte[] data, UUID creatorID)
        {
            AssetBase asset = new AssetBase(UUID.Random(), name, assetType, creatorID.ToString());
            asset.Description = description;
            asset.Data = (data == null) ? new byte[1] : data;

            return asset;
        }

        /// <summary>
        /// Move an item within the agent's inventory.
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="folderID"></param>
        /// <param name="itemID"></param>
        /// <param name="length"></param>
        /// <param name="newName"></param>
        public void MoveInventoryItem(IClientAPI remoteClient, List<InventoryItemBase> items)
        {
            //m_log.DebugFormat(
            //    "[AGENT INVENTORY]: Moving {0} items for user {1}", items.Count, remoteClient.AgentId);

            if (!InventoryService.MoveItems(remoteClient.AgentId, items))
                m_log.Warn("[AGENT INVENTORY]: Failed to move items for user " + remoteClient.AgentId);
        }

        /// <summary>
        /// Create a new inventory item.
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="folderID"></param>
        /// <param name="callbackID"></param>
        /// <param name="asset"></param>
        /// <param name="invType"></param>
        /// <param name="nextOwnerMask"></param>
        private void CreateNewInventoryItem(IClientAPI remoteClient, string creatorID, UUID folderID, string name, uint flags, uint callbackID,
                                            AssetBase asset, sbyte invType, uint nextOwnerMask, int creationDate)
        {
            CreateNewInventoryItem(
                remoteClient, creatorID, folderID, name, flags, callbackID, asset, invType,
                (uint)PermissionMask.All, (uint)PermissionMask.All, 0, nextOwnerMask, 0, creationDate);
        }

        /// <summary>
        /// Create a new Inventory Item
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="folderID"></param>
        /// <param name="callbackID"></param>
        /// <param name="asset"></param>
        /// <param name="invType"></param>
        /// <param name="nextOwnerMask"></param>
        /// <param name="creationDate"></param>
        private void CreateNewInventoryItem(
            IClientAPI remoteClient, string creatorID, UUID folderID, string name, uint flags, uint callbackID, AssetBase asset, sbyte invType,
            uint baseMask, uint currentMask, uint everyoneMask, uint nextOwnerMask, uint groupMask, int creationDate)
        {
            InventoryItemBase item = new InventoryItemBase();
            item.Owner = remoteClient.AgentId;
            item.CreatorId = creatorID;
            item.ID = UUID.Random();
            item.AssetID = asset.FullID;
            item.Description = asset.Description;
            item.Name = name;
            item.Flags = flags;
            item.AssetType = asset.Type;
            item.InvType = invType;
            item.Folder = folderID;
            item.CurrentPermissions = currentMask;
            item.NextPermissions = nextOwnerMask;
            item.EveryOnePermissions = everyoneMask;
            item.GroupPermissions = groupMask;
            item.BasePermissions = baseMask;
            item.CreationDate = creationDate;

            if (AddInventoryItem(item))
            {
                remoteClient.SendInventoryItemCreateUpdate(item, callbackID);
            }
            else
            {
                IDialogModule module = RequestModuleInterface<IDialogModule>();
                if (module != null)
                    module.SendAlertToUser(remoteClient, "Failed to create item");
                m_log.WarnFormat(
                    "Failed to add item for {0} in CreateNewInventoryItem!",
                     remoteClient.Name);
            }
        }

        /// <summary>
        /// Create a new inventory item.  Called when the client creates a new item directly within their
        /// inventory (e.g. by selecting a context inventory menu option).
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="transactionID"></param>
        /// <param name="folderID"></param>
        /// <param name="callbackID"></param>
        /// <param name="description"></param>
        /// <param name="name"></param>
        /// <param name="invType"></param>
        /// <param name="type"></param>
        /// <param name="wearableType"></param>
        /// <param name="nextOwnerMask"></param>
        public void CreateNewInventoryItem(IClientAPI remoteClient, UUID transactionID, UUID folderID,
                                           uint callbackID, string description, string name, sbyte invType,
                                           sbyte assetType,
                                           byte wearableType, uint nextOwnerMask, int creationDate)
        {
            //m_log.DebugFormat("[AGENT INVENTORY]: Received request to create inventory item {0} in folder {1}", name, folderID);

            if (!Permissions.CanCreateUserInventory(invType, remoteClient.AgentId))
                return;

            InventoryFolderBase f = new InventoryFolderBase(folderID, remoteClient.AgentId);
            InventoryFolderBase folder = InventoryService.GetFolder(f);

            if (folder == null || folder.Owner != remoteClient.AgentId)
                return;

            if (transactionID == UUID.Zero)
            {
                ScenePresence presence;
                if (TryGetScenePresence(remoteClient.AgentId, out presence))
                {
                    if (Permissions.CanTakeLandmark(remoteClient.AgentId))
                    {
                        byte[] data = null;

                        if (invType == (sbyte)InventoryType.Landmark && presence != null)
                        {
                            Vector3 pos = presence.AbsolutePosition;
                            string strdata = String.Format(
                                "Landmark version 2\nregion_id {0}\nlocal_pos {1} {2} {3}\nregion_handle {4}\n",
                                presence.Scene.RegionInfo.RegionID,
                                pos.X, pos.Y, pos.Z,
                                presence.RegionHandle);
                            data = Encoding.ASCII.GetBytes(strdata);
                        }
                        if (invType == (sbyte)InventoryType.LSL)
                        {
                            data = Encoding.ASCII.GetBytes(DefaultLSLScript);
                        }

                        AssetBase asset = CreateAsset(name, description, assetType, data, remoteClient.AgentId);
                        AssetService.Store(asset);

                        CreateNewInventoryItem(remoteClient, remoteClient.AgentId.ToString(), folderID, asset.Name, 0, callbackID, asset, invType, nextOwnerMask, creationDate);
                    }
                }
                else
                {
                    m_log.ErrorFormat(
                        "ScenePresence for agent uuid {0} unexpectedly not found in CreateNewInventoryItem",
                        remoteClient.AgentId);
                }
            }
            else
            {
                IAgentAssetTransactions agentTransactions = this.RequestModuleInterface<IAgentAssetTransactions>();
                if (agentTransactions != null)
                {
                    agentTransactions.HandleItemCreationFromTransaction(
                        remoteClient, transactionID, folderID, callbackID, description,
                        name, invType, assetType, wearableType, nextOwnerMask);
                }
            }
        }

        private void HandleLinkInventoryItem(IClientAPI remoteClient, UUID transActionID, UUID folderID,
                                             uint callbackID, string description, string name,
                                             sbyte invType, sbyte type, UUID olditemID)
        {
            //m_log.DebugFormat("[AGENT INVENTORY]: Received request to create inventory item link {0} in folder {1} pointing to {2}", name, folderID, olditemID);

            if (!Permissions.CanCreateUserInventory(invType, remoteClient.AgentId))
                return;

            ScenePresence presence;
            if (TryGetScenePresence(remoteClient.AgentId, out presence))
            {
                AssetBase asset = new AssetBase();
                asset.FullID = olditemID;
                asset.Type = type;
                asset.Name = name;
                asset.Description = description;
                
                CreateNewInventoryItem(
                    remoteClient, remoteClient.AgentId.ToString(), folderID, name, 0, callbackID, asset, invType, 
                    (uint)PermissionMask.All, (uint)PermissionMask.All, (uint)PermissionMask.All, 
                    (uint)PermissionMask.All, (uint)PermissionMask.All, Util.UnixTimeSinceEpoch());
            }
            else
            {
                m_log.ErrorFormat(
                    "ScenePresence for agent uuid {0} unexpectedly not found in HandleLinkInventoryItem",
                    remoteClient.AgentId);
            }
        }

        /// <summary>
        /// Remove an inventory item for the client's inventory
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="itemID"></param>
        private void RemoveInventoryItem(IClientAPI remoteClient, List<UUID> itemIDs)
        {
            //m_log.Debug("[SCENE INVENTORY]: user " + remoteClient.AgentId);
            InventoryService.DeleteItems(remoteClient.AgentId, itemIDs);
        }

        /// <summary>
        /// Removes an inventory folder.  This packet is sent when the user
        /// right-clicks a folder that's already in trash and chooses "purge"
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="folderID"></param>
        private void RemoveInventoryFolder(IClientAPI remoteClient, List<UUID> folderIDs)
        {
            m_log.DebugFormat("[SCENE INVENTORY]: RemoveInventoryFolders count {0}", folderIDs.Count);
            InventoryService.DeleteFolders(remoteClient.AgentId, folderIDs);
        }

        /// <summary>
        /// Send the details of a prim's inventory to the client.
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="primLocalID"></param>
        public void RequestTaskInventory(IClientAPI remoteClient, uint primLocalID)
        {
            SceneObjectGroup group = GetGroupByPrim(primLocalID);
            if (group != null)
            {
                IXfer xfer = RequestModuleInterface<IXfer>();
                if (xfer != null)
                    group.RequestInventoryFile(remoteClient, primLocalID, xfer);
            }
            else
            {
                m_log.ErrorFormat(
                    "[PRIM INVENTORY]: Inventory requested of prim {0} which doesn't exist", primLocalID);
            }
        }

        /// <summary>
        /// Remove an item from a prim (task) inventory
        /// </summary>
        /// <param name="remoteClient">Unused at the moment but retained since the avatar ID might
        /// be necessary for a permissions check at some stage.</param>
        /// <param name="itemID"></param>
        /// <param name="localID"></param>
        public void RemoveTaskInventory(IClientAPI remoteClient, UUID itemID, uint localID)
        {
            SceneObjectPart part = GetSceneObjectPart(localID);
            if (Permissions.CanDeleteObjectInventory(itemID, part.UUID, remoteClient.AgentId))
            {
                SceneObjectGroup group = part.ParentGroup;
                if (group != null)
                {
                    TaskInventoryItem item = group.GetInventoryItem(localID, itemID);
                    if (item == null)
                        return;

                    if (item.Type == 10)
                    {
                        part.RemoveScriptEvents(itemID);
                        EventManager.TriggerRemoveScript(localID, itemID);
                    }

                    group.RemoveInventoryItem(localID, itemID);
                    part.GetProperties(remoteClient);
                }
                else
                {
                    m_log.ErrorFormat(
                        "[PRIM INVENTORY]: " +
                        "Removal of item {0} requested of prim {1} but this prim does not exist",
                        itemID,
                        localID);
                }
            }
        }

        private InventoryItemBase CreateAgentInventoryItemFromTask(UUID destAgent, SceneObjectPart part, UUID itemId)
        {
            TaskInventoryItem taskItem = part.Inventory.GetInventoryItem(itemId);

            if (null == taskItem)
            {
                m_log.ErrorFormat(
                    "[PRIM INVENTORY]: Tried to retrieve item ID {0} from prim {1}, {2} for creating an avatar"
                        + " inventory item from a prim's inventory item "
                        + " but the required item does not exist in the prim's inventory",
                    itemId, part.Name, part.UUID);

                return null;
            }

            if ((destAgent != taskItem.OwnerID) && ((taskItem.CurrentPermissions & (uint)PermissionMask.Transfer) == 0))
            {
                return null;
            }

            InventoryItemBase agentItem = new InventoryItemBase();

            agentItem.ID = UUID.Random();
            agentItem.CreatorId = taskItem.CreatorID.ToString();
            agentItem.Owner = destAgent;
            agentItem.AssetID = taskItem.AssetID;
            agentItem.Description = taskItem.Description;
            agentItem.Name = taskItem.Name;
            agentItem.AssetType = taskItem.Type;
            agentItem.InvType = taskItem.InvType;
            agentItem.Flags = taskItem.Flags;
            agentItem.SalePrice = taskItem.SalePrice;
            agentItem.SaleType = taskItem.SaleType;

            if ((part.OwnerID != destAgent) && Permissions.PropagatePermissions())
            {
                agentItem.BasePermissions = taskItem.BasePermissions & (taskItem.NextPermissions | (uint)PermissionMask.Move);
                if (taskItem.InvType == (int)InventoryType.Object)
                    agentItem.CurrentPermissions = agentItem.BasePermissions & (((taskItem.CurrentPermissions & 7) << 13) | (taskItem.CurrentPermissions & (uint)PermissionMask.Move));
                else
                    agentItem.CurrentPermissions = agentItem.BasePermissions & taskItem.CurrentPermissions;

                agentItem.CurrentPermissions |= 16; // Slam
                agentItem.NextPermissions = taskItem.NextPermissions;
                agentItem.EveryOnePermissions = taskItem.EveryonePermissions & (taskItem.NextPermissions | (uint)PermissionMask.Move);
                agentItem.GroupPermissions = taskItem.GroupPermissions & taskItem.NextPermissions;
            }
            else
            {
                agentItem.BasePermissions = taskItem.BasePermissions;
                agentItem.CurrentPermissions = taskItem.CurrentPermissions;
                agentItem.NextPermissions = taskItem.NextPermissions;
                agentItem.EveryOnePermissions = taskItem.EveryonePermissions;
                agentItem.GroupPermissions = taskItem.GroupPermissions;
            }

            if (!Permissions.BypassPermissions())
            {
                if ((taskItem.CurrentPermissions & (uint)PermissionMask.Copy) == 0)
                    part.Inventory.RemoveInventoryItem(itemId);
            }

            return agentItem;
        }

        /// <summary>
        /// Move the given item in the given prim to a folder in the client's inventory
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="folderID"></param>
        /// <param name="part"></param>
        /// <param name="itemID"></param>
        public InventoryItemBase MoveTaskInventoryItem(IClientAPI remoteClient, UUID folderId, SceneObjectPart part, UUID itemId)
        {
            m_log.DebugFormat(
                "[PRIM INVENTORY]: Adding item {0} from {1} to folder {2} for {3}", 
                itemId, part.Name, folderId, remoteClient.Name);
            
            InventoryItemBase agentItem = CreateAgentInventoryItemFromTask(remoteClient.AgentId, part, itemId);
            if (Permissions.CanCopyObjectInventory(itemId, part.UUID, remoteClient.AgentId))
            {

                if (agentItem == null)
                    return null;

                agentItem.Folder = folderId;
                AddInventoryItem(remoteClient, agentItem);
                return agentItem;
            }
            return null;
        }

        /// <summary>
        /// <see>ClientMoveTaskInventoryItem</see>
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="folderID"></param>
        /// <param name="primLocalID"></param>
        /// <param name="itemID"></param>
        public void ClientMoveTaskInventoryItem(IClientAPI remoteClient, UUID folderId, uint primLocalId, UUID itemId)
        {
            SceneObjectPart part = GetSceneObjectPart(primLocalId);

            if (null == part)
            {
                m_log.WarnFormat(
                    "[PRIM INVENTORY]: " +
                    "Move of inventory item {0} from prim with local id {1} failed because the prim could not be found",
                    itemId, primLocalId);

                return;
            }

            TaskInventoryItem taskItem = part.Inventory.GetInventoryItem(itemId);

            if (null == taskItem)
            {
                m_log.WarnFormat("[PRIM INVENTORY]: Move of inventory item {0} from prim with local id {1} failed"
                    + " because the inventory item could not be found",
                    itemId, primLocalId);

                return;
            }

            TaskInventoryItem item = part.Inventory.GetInventoryItem(itemId);
            if ((item.CurrentPermissions & (uint)PermissionMask.Copy) == 0)
            {
                // If the item to be moved is no copy, we need to be able to
                // edit the prim.
                if (!Permissions.CanEditObjectInventory(part.UUID, remoteClient.AgentId))
                    return;
            }
            else
            {
                // If the item is copiable, then we just need to have perms
                // on it. The delete check is a pure rights check
                if (!Permissions.CanDeleteObject(part.UUID, remoteClient.AgentId))
                    return;
            }

            MoveTaskInventoryItem(remoteClient, folderId, part, itemId);
        }

        /// <summary>
        /// <see>MoveTaskInventoryItem</see>
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="folderID">
        /// The user inventory folder to move (or copy) the item to.  If null, then the most
        /// suitable system folder is used (e.g. the Objects folder for objects).  If there is no suitable folder, then
        /// the item is placed in the user's root inventory folder
        /// </param>
        /// <param name="part"></param>
        /// <param name="itemID"></param>
        public InventoryItemBase MoveTaskInventoryItem(UUID avatarId, UUID folderId, SceneObjectPart part, UUID itemId)
        {
            ScenePresence avatar;

            if (TryGetScenePresence(avatarId, out avatar))
            {
                return MoveTaskInventoryItem(avatar.ControllingClient, folderId, part, itemId);
            }
            else
            {
                InventoryItemBase agentItem = CreateAgentInventoryItemFromTask(avatarId, part, itemId);

                if (agentItem == null)
                    return null;

                agentItem.Folder = folderId;

                AddInventoryItem(agentItem);

                return agentItem;
            }
        }

        /// <summary>
        /// Copy a task (prim) inventory item to another task (prim)
        /// </summary>
        /// <param name="destId"></param>
        /// <param name="part"></param>
        /// <param name="itemId"></param>
        public void MoveTaskInventoryItem(UUID destId, SceneObjectPart part, UUID itemId)
        {
            TaskInventoryItem srcTaskItem = part.Inventory.GetInventoryItem(itemId);

            if (srcTaskItem == null)
            {
                m_log.ErrorFormat(
                    "[PRIM INVENTORY]: Tried to retrieve item ID {0} from prim {1}, {2} for moving"
                        + " but the item does not exist in this inventory",
                    itemId, part.Name, part.UUID);

                return;
            }

            SceneObjectPart destPart = GetSceneObjectPart(destId);

            if (destPart == null)
            {
                m_log.ErrorFormat(
                        "[PRIM INVENTORY]: " +
                        "Could not find prim for ID {0}",
                        destId);
                return;
            }

            // Can't transfer this
            //
            if ((part.OwnerID != destPart.OwnerID) && ((srcTaskItem.CurrentPermissions & (uint)PermissionMask.Transfer) == 0))
                return;

            if (part.OwnerID != destPart.OwnerID && (part.GetEffectiveObjectFlags() & (uint)PrimFlags.AllowInventoryDrop) == 0)
            {
                // object cannot copy items to an object owned by a different owner
                // unless llAllowInventoryDrop has been called

                return;
            }

            // must have both move and modify permission to put an item in an object
            if ((part.OwnerMask & ((uint)PermissionMask.Move | (uint)PermissionMask.Modify)) == 0)
            {
                return;
            }

            TaskInventoryItem destTaskItem = new TaskInventoryItem();

            destTaskItem.ItemID = UUID.Random();
            destTaskItem.CreatorID = srcTaskItem.CreatorID;
            destTaskItem.AssetID = srcTaskItem.AssetID;
            destTaskItem.GroupID = destPart.GroupID;
            destTaskItem.OwnerID = destPart.OwnerID;
            destTaskItem.ParentID = destPart.UUID;
            destTaskItem.ParentPartID = destPart.UUID;

            destTaskItem.BasePermissions = srcTaskItem.BasePermissions;
            destTaskItem.EveryonePermissions = srcTaskItem.EveryonePermissions;
            destTaskItem.GroupPermissions = srcTaskItem.GroupPermissions;
            destTaskItem.CurrentPermissions = srcTaskItem.CurrentPermissions;
            destTaskItem.NextPermissions = srcTaskItem.NextPermissions;
            destTaskItem.Flags = srcTaskItem.Flags;
            destTaskItem.SalePrice = srcTaskItem.SalePrice;
            destTaskItem.SaleType = srcTaskItem.SaleType;

            if (destPart.OwnerID != part.OwnerID)
            {
                if (Permissions.PropagatePermissions())
                {
                    destTaskItem.CurrentPermissions = srcTaskItem.CurrentPermissions &
                            (srcTaskItem.NextPermissions | (uint)PermissionMask.Move);
                    destTaskItem.GroupPermissions = srcTaskItem.GroupPermissions &
                            (srcTaskItem.NextPermissions | (uint)PermissionMask.Move);
                    destTaskItem.EveryonePermissions = srcTaskItem.EveryonePermissions &
                            (srcTaskItem.NextPermissions | (uint)PermissionMask.Move);
                    destTaskItem.BasePermissions = srcTaskItem.BasePermissions &
                            (srcTaskItem.NextPermissions | (uint)PermissionMask.Move);
                    destTaskItem.CurrentPermissions |= 16; // Slam!
                }
            }

            destTaskItem.Description = srcTaskItem.Description;
            destTaskItem.Name = srcTaskItem.Name;
            destTaskItem.InvType = srcTaskItem.InvType;
            destTaskItem.Type = srcTaskItem.Type;

            destPart.Inventory.AddInventoryItem(destTaskItem, part.OwnerID != destPart.OwnerID);

            if ((srcTaskItem.CurrentPermissions & (uint)PermissionMask.Copy) == 0)
                part.Inventory.RemoveInventoryItem(itemId);

            ScenePresence avatar;

            if (TryGetScenePresence(srcTaskItem.OwnerID, out avatar))
            {
                destPart.GetProperties(avatar.ControllingClient);
            }
        }

        public UUID MoveTaskInventoryItems(UUID destID, string category, SceneObjectPart host, List<UUID> items)
        {
            InventoryFolderBase rootFolder = InventoryService.GetRootFolder(destID);

            UUID newFolderID = UUID.Random();

            InventoryFolderBase newFolder = new InventoryFolderBase(newFolderID, category, destID, -1, rootFolder.ID, rootFolder.Version);
            InventoryService.AddFolder(newFolder);

            foreach (UUID itemID in items)
            {
                InventoryItemBase agentItem = CreateAgentInventoryItemFromTask(destID, host, itemID);

                if (agentItem != null)
                {
                    agentItem.Folder = newFolderID;

                    AddInventoryItem(agentItem);
                }
            }

            ScenePresence avatar = null;
            if (TryGetScenePresence(destID, out avatar))
            {
                //profile.SendInventoryDecendents(avatar.ControllingClient,
                //        profile.RootFolder.ID, true, false);
                //profile.SendInventoryDecendents(avatar.ControllingClient,
                //        newFolderID, false, true);

                SendInventoryUpdate(avatar.ControllingClient, rootFolder, true, false);
                SendInventoryUpdate(avatar.ControllingClient, newFolder, false, true);
            }

            return newFolderID;
        }

        private void SendInventoryUpdate(IClientAPI client, InventoryFolderBase folder, bool fetchFolders, bool fetchItems)
        {
            if (folder == null)
                return;

            // TODO: This code for looking in the folder for the library should be folded somewhere else
            // so that this class doesn't have to know the details (and so that multiple libraries, etc.
            // can be handled transparently).
            InventoryFolderImpl fold = null;
            if (LibraryService != null && LibraryService.LibraryRootFolder != null)
            {
                if ((fold = LibraryService.LibraryRootFolder.FindFolder(folder.ID)) != null)
                {
                    client.SendInventoryFolderDetails(
                        fold.Owner, folder.ID, fold.RequestListOfItems(),
                        fold.RequestListOfFolders(), fold.Version, fetchFolders, fetchItems);
                    return;
                }
            }

            // Fetch the folder contents
            InventoryCollection contents = InventoryService.GetFolderContent(client.AgentId, folder.ID);

            // Fetch the folder itself to get its current version
            InventoryFolderBase containingFolder = new InventoryFolderBase(folder.ID, client.AgentId);
            containingFolder = InventoryService.GetFolder(containingFolder);

            //m_log.DebugFormat("[AGENT INVENTORY]: Sending inventory folder contents ({0} nodes) for \"{1}\" to {2} {3}",
            //    contents.Folders.Count + contents.Items.Count, containingFolder.Name, client.FirstName, client.LastName);

            if (containingFolder != null && containingFolder != null)
                client.SendInventoryFolderDetails(client.AgentId, folder.ID, contents.Items, contents.Folders, containingFolder.Version, fetchFolders, fetchItems);
        }

        /// <summary>
        /// Update an item in a prim (task) inventory.
        /// This method does not handle scripts, <see>RezScript(IClientAPI, UUID, unit)</see>
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="transactionID"></param>
        /// <param name="itemInfo"></param>
        /// <param name="primLocalID"></param>
        public void UpdateTaskInventory(IClientAPI remoteClient, UUID transactionID, TaskInventoryItem itemInfo,
                                        uint primLocalID)
        {
            UUID itemID = itemInfo.ItemID;

            // Find the prim we're dealing with
            SceneObjectPart part = GetSceneObjectPart(primLocalID);

            if (part != null)
            {
                TaskInventoryItem currentItem = part.Inventory.GetInventoryItem(itemID);
                bool allowInventoryDrop = (part.GetEffectiveObjectFlags()
                                           & (uint)PrimFlags.AllowInventoryDrop) != 0;

                // Explicity allow anyone to add to the inventory if the
                // AllowInventoryDrop flag has been set. Don't however let
                // them update an item unless they pass the external checks
                //
                if (!Permissions.CanEditObjectInventory(part.UUID, remoteClient.AgentId)
                    && (currentItem != null || !allowInventoryDrop))
                    return;

                if (currentItem == null)
                {
                    UUID copyID = UUID.Random();
                    if (itemID != UUID.Zero)
                    {
                        InventoryItemBase item = new InventoryItemBase(itemID, remoteClient.AgentId);
                        item = InventoryService.GetItem(item);

                        // Try library
                        if (null == item && LibraryService != null && LibraryService.LibraryRootFolder != null)
                        {
                            item = LibraryService.LibraryRootFolder.FindItem(itemID);
                        }

                        // If we've found the item in the user's inventory or in the library
                        if (item != null)
                        {
                            part.ParentGroup.AddInventoryItem(remoteClient, primLocalID, item, copyID);
                            m_log.InfoFormat(
                                "[PRIM INVENTORY]: Update with item {0} requested of prim {1} for {2}",
                                item.Name, primLocalID, remoteClient.Name);
                            part.GetProperties(remoteClient);
                            if (!Permissions.BypassPermissions())
                            {
                                if ((item.CurrentPermissions & (uint)PermissionMask.Copy) == 0)
                                {
                                    List<UUID> uuids = new List<UUID>();
                                    uuids.Add(itemID);
                                    RemoveInventoryItem(remoteClient, uuids);
                                }
                            }
                        }
                        else
                        {
                            m_log.ErrorFormat(
                                "[PRIM INVENTORY]: Could not find inventory item {0} to update for {1}!",
                                itemID, remoteClient.Name);
                        }
                    }
                }
                else // Updating existing item with new perms etc
                {
                    IAgentAssetTransactions agentTransactions = this.RequestModuleInterface<IAgentAssetTransactions>();
                    if (agentTransactions != null)
                    {
                        agentTransactions.HandleTaskItemUpdateFromTransaction(
                            remoteClient, part, transactionID, currentItem);

                        if ((InventoryType)itemInfo.InvType == InventoryType.Notecard) 
                            remoteClient.SendAgentAlertMessage("Notecard saved", false);
                        else if ((InventoryType)itemInfo.InvType == InventoryType.LSL)
                            remoteClient.SendAgentAlertMessage("Script saved", false);
                        else
                            remoteClient.SendAgentAlertMessage("Item saved", false);
                    }

                    // Base ALWAYS has move
                    currentItem.BasePermissions |= (uint)PermissionMask.Move;

                    // Check if we're allowed to mess with permissions
                    if (!Permissions.IsGod(remoteClient.AgentId)) // Not a god
                    {
                        if (remoteClient.AgentId != part.OwnerID) // Not owner
                        {
                            // Friends and group members can't change any perms
                            itemInfo.BasePermissions = currentItem.BasePermissions;
                            itemInfo.EveryonePermissions = currentItem.EveryonePermissions;
                            itemInfo.GroupPermissions = currentItem.GroupPermissions;
                            itemInfo.NextPermissions = currentItem.NextPermissions;
                            itemInfo.CurrentPermissions = currentItem.CurrentPermissions;
                        }
                        else
                        {
                            // Owner can't change base, and can change other
                            // only up to base
                            itemInfo.BasePermissions = currentItem.BasePermissions;
                            itemInfo.EveryonePermissions &= currentItem.BasePermissions;
                            itemInfo.GroupPermissions &= currentItem.BasePermissions;
                            itemInfo.CurrentPermissions &= currentItem.BasePermissions;
                            itemInfo.NextPermissions &= currentItem.BasePermissions;
                        }

                    }

                    // Next ALWAYS has move
                    itemInfo.NextPermissions |= (uint)PermissionMask.Move;

                    if (part.Inventory.UpdateInventoryItem(itemInfo))
                    {
                        part.GetProperties(remoteClient);
                    }
                }
            }
            else
            {
                m_log.WarnFormat(
                    "[PRIM INVENTORY]: " +
                    "Update with item {0} requested of prim {1} for {2} but this prim does not exist",
                    itemID, primLocalID, remoteClient.Name);
            }
        }

        public string DefaultLSLScript = "default\n{\n    state_entry()\n    {\n        llSay(0, \"Script running.\");\n    }\n    touch_start(integer number)\n    {\n        llSay(0,\"Touched.\");\n    }\n}\n";

        /// <summary>
        /// Rez a script into a prim's inventory, either ex nihilo or from an existing avatar inventory
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="itemID"> </param>
        /// <param name="localID"></param>
        public void RezScript(IClientAPI remoteClient, InventoryItemBase itemBase, UUID transactionID, uint localID)
        {
            UUID itemID = itemBase.ID;
            UUID copyID = UUID.Random();

            if (itemID != UUID.Zero)  // transferred from an avatar inventory to the prim's inventory
            {
                InventoryItemBase item = new InventoryItemBase(itemID, remoteClient.AgentId);
                item = InventoryService.GetItem(item);

                // Try library
                // XXX clumsy, possibly should be one call
                if (null == item && LibraryService != null && LibraryService.LibraryRootFolder != null)
                {
                    item = LibraryService.LibraryRootFolder.FindItem(itemID);
                }

                if (item != null)
                {
                    SceneObjectPart part = GetSceneObjectPart(localID);
                    if (part != null)
                    {
                        if (!Permissions.CanEditObjectInventory(part.UUID, remoteClient.AgentId))
                            return;

                        part.ParentGroup.AddInventoryItem(remoteClient, localID, item, copyID);
                        part.Inventory.CreateScriptInstance(copyID, 0, false, DefaultScriptEngine, 0);

                        //                        m_log.InfoFormat("[PRIMINVENTORY]: " +
                        //                                         "Rezzed script {0} into prim local ID {1} for user {2}",
                        //                                         item.inventoryName, localID, remoteClient.Name);
                        part.GetProperties(remoteClient);
                        part.ParentGroup.ResumeScripts();
                    }
                    else
                    {
                        m_log.ErrorFormat(
                            "[PRIM INVENTORY]: " +
                            "Could not rez script {0} into prim local ID {1} for user {2}"
                            + " because the prim could not be found in the region!",
                            item.Name, localID, remoteClient.Name);
                    }
                }
                else
                {
                    m_log.ErrorFormat(
                        "[PRIM INVENTORY]: Could not find script inventory item {0} to rez for {1}!",
                        itemID, remoteClient.Name);
                }
            }
            else  // script has been rezzed directly into a prim's inventory
            {
                SceneObjectPart part = GetSceneObjectPart(itemBase.Folder);
                if (part == null)
                    return;

                if (!Permissions.CanCreateObjectInventory(
                    itemBase.InvType, part.UUID, remoteClient.AgentId))
                    return;

                AssetBase asset = CreateAsset(itemBase.Name, itemBase.Description, (sbyte)itemBase.AssetType,
                    Encoding.ASCII.GetBytes(DefaultLSLScript),
                    remoteClient.AgentId);
                AssetService.Store(asset);

                TaskInventoryItem taskItem = new TaskInventoryItem();

                taskItem.ResetIDs(itemBase.Folder);
                taskItem.ParentID = itemBase.Folder;
                taskItem.CreationDate = (uint)itemBase.CreationDate;
                taskItem.Name = itemBase.Name;
                taskItem.Description = itemBase.Description;
                taskItem.Type = itemBase.AssetType;
                taskItem.InvType = itemBase.InvType;
                taskItem.OwnerID = itemBase.Owner;
                taskItem.CreatorID = itemBase.CreatorIdAsUuid;
                taskItem.BasePermissions = itemBase.BasePermissions;
                taskItem.CurrentPermissions = itemBase.CurrentPermissions;
                taskItem.EveryonePermissions = itemBase.EveryOnePermissions;
                taskItem.GroupPermissions = itemBase.GroupPermissions;
                taskItem.NextPermissions = itemBase.NextPermissions;
                taskItem.GroupID = itemBase.GroupID;
                taskItem.GroupPermissions = 0;
                taskItem.Flags = itemBase.Flags;
                taskItem.PermsGranter = UUID.Zero;
                taskItem.PermsMask = 0;
                taskItem.AssetID = asset.FullID;
                taskItem.SalePrice = itemBase.SalePrice;
                taskItem.SaleType = itemBase.SaleType;

                part.Inventory.AddInventoryItem(taskItem, false);
                part.GetProperties(remoteClient);

                part.Inventory.CreateScriptInstance(taskItem, 0, false, DefaultScriptEngine, 0);
                part.ParentGroup.ResumeScripts();
            }
        }

        /// <summary>
        /// Rez a script into a prim's inventory from another prim
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="itemID"> </param>
        /// <param name="localID"></param>
        public void RezScript(UUID srcId, SceneObjectPart srcPart, UUID destId, int pin, int running, int start_param)
        {
            TaskInventoryItem srcTaskItem = srcPart.Inventory.GetInventoryItem(srcId);

            if (srcTaskItem == null)
            {
                m_log.ErrorFormat(
                    "[PRIM INVENTORY]: Tried to retrieve item ID {0} from prim {1}, {2} for rezzing a script but the "
                        + " item does not exist in this inventory",
                    srcId, srcPart.Name, srcPart.UUID);

                return;
            }

            SceneObjectPart destPart = GetSceneObjectPart(destId);

            if (destPart == null)
            {
                m_log.ErrorFormat(
                        "[PRIM INVENTORY]: " +
                        "Could not find script for ID {0}",
                        destId);
                return;
            }
        
            // Must own the object, and have modify rights
            if (srcPart.OwnerID != destPart.OwnerID)
            {
                // Group permissions
                if ((destPart.GroupID == UUID.Zero) || (destPart.GroupID != srcPart.GroupID) ||
                    ((destPart.GroupMask & (uint)PermissionMask.Modify) == 0))
                    return;
            } else {
                if ((destPart.OwnerMask & (uint)PermissionMask.Modify) == 0)
                    return;
            }

            if (destPart.ScriptAccessPin != pin)
            {
                m_log.WarnFormat(
                        "[PRIM INVENTORY]: " +
                        "Script in object {0} : {1}, attempted to load script {2} : {3} into object {4} : {5} with invalid pin {6}",
                        srcPart.Name, srcId, srcTaskItem.Name, srcTaskItem.ItemID, destPart.Name, destId, pin);
                // the LSL Wiki says we are supposed to shout on the DEBUG_CHANNEL -
                //   "Object: Task Object trying to illegally load script onto task Other_Object!"
                // How do we shout from in here?
                return;
            }

            TaskInventoryItem destTaskItem = new TaskInventoryItem();

            destTaskItem.ItemID = UUID.Random();
            destTaskItem.CreatorID = srcTaskItem.CreatorID;
            destTaskItem.AssetID = srcTaskItem.AssetID;
            destTaskItem.GroupID = destPart.GroupID;
            destTaskItem.OwnerID = destPart.OwnerID;
            destTaskItem.ParentID = destPart.UUID;
            destTaskItem.ParentPartID = destPart.UUID;

            destTaskItem.BasePermissions = srcTaskItem.BasePermissions;
            destTaskItem.EveryonePermissions = srcTaskItem.EveryonePermissions;
            destTaskItem.GroupPermissions = srcTaskItem.GroupPermissions;
            destTaskItem.CurrentPermissions = srcTaskItem.CurrentPermissions;
            destTaskItem.NextPermissions = srcTaskItem.NextPermissions;
            destTaskItem.Flags = srcTaskItem.Flags;
            destTaskItem.SalePrice = srcTaskItem.SalePrice;
            destTaskItem.SaleType = srcTaskItem.SaleType;

            if (destPart.OwnerID != srcPart.OwnerID)
            {
                if (Permissions.PropagatePermissions())
                {
                    destTaskItem.CurrentPermissions = srcTaskItem.CurrentPermissions &
                            srcTaskItem.NextPermissions;
                    destTaskItem.GroupPermissions = srcTaskItem.GroupPermissions &
                            srcTaskItem.NextPermissions;
                    destTaskItem.EveryonePermissions = srcTaskItem.EveryonePermissions &
                            srcTaskItem.NextPermissions;
                    destTaskItem.BasePermissions = srcTaskItem.BasePermissions &
                            srcTaskItem.NextPermissions;
                    destTaskItem.CurrentPermissions |= 16; // Slam!
                }
            }

            destTaskItem.Description = srcTaskItem.Description;
            destTaskItem.Name = srcTaskItem.Name;
            destTaskItem.InvType = srcTaskItem.InvType;
            destTaskItem.Type = srcTaskItem.Type;

            destPart.Inventory.AddInventoryItemExclusive(destTaskItem, false);

            if (running > 0)
            {
                destPart.Inventory.CreateScriptInstance(destTaskItem, start_param, false, DefaultScriptEngine, 0);
            }

            destPart.ParentGroup.ResumeScripts();

            ScenePresence avatar;

            if (TryGetScenePresence(srcTaskItem.OwnerID, out avatar))
            {
                destPart.GetProperties(avatar.ControllingClient);
            }
        }

        public virtual void DeRezObjects(IClientAPI remoteClient, List<uint> localIDs,
                UUID groupID, DeRezAction action, UUID destinationID)
        {
            // First, see of we can perform the requested action and
            // build a list of eligible objects
            List<uint> deleteIDs = new List<uint>();
            List<SceneObjectGroup> deleteGroups = new List<SceneObjectGroup>();

            // Start with true for both, then remove the flags if objects
            // that we can't derez are part of the selection
            bool permissionToTake = true;
            bool permissionToTakeCopy = true;
            bool permissionToDelete = true;

            foreach (uint localID in localIDs)
            {
                // Invalid id
                SceneObjectPart part = GetSceneObjectPart(localID);
                if (part == null)
                    continue;

                // Already deleted by someone else
                if (part.ParentGroup == null || part.ParentGroup.IsDeleted)
                    continue;

                // Can't delete child prims
                if (part != part.ParentGroup.RootPart)
                    continue;

                SceneObjectGroup grp = part.ParentGroup;

                deleteIDs.Add(localID);
                deleteGroups.Add(grp);

                ScenePresence SP = remoteClient == null ? null : GetScenePresence(remoteClient.AgentId);

                if (SP == null)
                {
                    // Autoreturn has a null client. Nothing else does. So
                    // allow only returns
                    if (action != DeRezAction.Return)
                        return;

                    permissionToTakeCopy = false;
                }
                else
                {
                    if (!Permissions.CanTakeCopyObject(grp.UUID, SP.UUID))
                        permissionToTakeCopy = false;
                    if (!Permissions.CanTakeObject(grp.UUID, SP.UUID))
                        permissionToTake = false;

                    if (!Permissions.CanDeleteObject(grp.UUID, SP.UUID))
                        permissionToDelete = false;
                }

            }

            // Handle god perms
            if ((remoteClient != null) && Permissions.IsGod(remoteClient.AgentId))
            {
                permissionToTake = true;
                permissionToTakeCopy = true;
                permissionToDelete = true;
            }

            // If we're re-saving, we don't even want to delete
            if (action == DeRezAction.SaveToExistingUserInventoryItem)
                permissionToDelete = false;

            // if we want to take a copy, we also don't want to delete
            // Note: after this point, the permissionToTakeCopy flag
            // becomes irrelevant. It already includes the permissionToTake
            // permission and after excluding no copy items here, we can
            // just use that.
            if (action == DeRezAction.TakeCopy)
            {
                // If we don't have permission, stop right here
                if (!permissionToTakeCopy)
                    return;

                permissionToTake = true;
                // Don't delete
                permissionToDelete = false;
            }

            if (action == DeRezAction.Return)
            {
                if (remoteClient != null && Permissions.CanReturnObjects(
                                    null,
                                    remoteClient.AgentId,
                                    deleteGroups))
                {
                    permissionToTake = true;
                    permissionToDelete = true;

                    AddReturns(deleteGroups[0].OwnerID, deleteGroups[0].Name, deleteGroups.Count, deleteGroups[0].AbsolutePosition, "parcel owner return", deleteGroups);
                }
                else // Auto return passes through here with null agent
                {
                    permissionToTake = true;
                    permissionToDelete = true;
                }
            }

            //if (permissionToTake)
            //{
                m_asyncSceneObjectDeleter.DeleteToInventory(
                    action, destinationID, deleteGroups, remoteClient == null ? UUID.Zero : remoteClient.AgentId,
                        permissionToDelete, permissionToTake);
            //}
            //else if (permissionToDelete)
            //{
            //    foreach (SceneObjectGroup g in deleteGroups)
            //        DeleteSceneObject(g, false, true);
            //}
        }

        public UUID attachObjectAssetStore(IClientAPI remoteClient, SceneObjectGroup grp, UUID AgentId, out UUID itemID)
        {
            itemID = UUID.Zero;
            if (grp != null)
            {
                Vector3 inventoryStoredPosition = new Vector3
                       (((grp.AbsolutePosition.X > (int)Constants.RegionSize)
                             ? 250
                             : grp.AbsolutePosition.X)
                        ,
                        (grp.AbsolutePosition.X > (int)Constants.RegionSize)
                            ? 250
                            : grp.AbsolutePosition.X,
                        grp.AbsolutePosition.Z);

                Vector3 originalPosition = grp.AbsolutePosition;

                grp.AbsolutePosition = inventoryStoredPosition;

                string sceneObjectXml = SceneObjectSerializer.ToOriginalXmlFormat(grp);

                grp.AbsolutePosition = originalPosition;

                AssetBase asset = CreateAsset(
                    grp.GetPartName(grp.LocalId),
                    grp.GetPartDescription(grp.LocalId),
                    (sbyte)AssetType.Object,
                    Utils.StringToBytes(sceneObjectXml),
                    remoteClient.AgentId);
                AssetService.Store(asset);

                InventoryItemBase item = new InventoryItemBase();
                item.CreatorId = grp.RootPart.CreatorID.ToString();
                item.Owner = remoteClient.AgentId;
                item.ID = UUID.Random();
                item.AssetID = asset.FullID;
                item.Description = asset.Description;
                item.Name = asset.Name;
                item.AssetType = asset.Type;
                item.InvType = (int)InventoryType.Object;

                InventoryFolderBase folder = InventoryService.GetFolderForType(remoteClient.AgentId, AssetType.Object);
                if (folder != null)
                    item.Folder = folder.ID;
                else // oopsies
                    item.Folder = UUID.Zero;

                if ((remoteClient.AgentId != grp.RootPart.OwnerID) && Permissions.PropagatePermissions())
                {
                    item.BasePermissions = grp.RootPart.NextOwnerMask;
                    item.CurrentPermissions = grp.RootPart.NextOwnerMask;
                    item.NextPermissions = grp.RootPart.NextOwnerMask;
                    item.EveryOnePermissions = grp.RootPart.EveryoneMask & grp.RootPart.NextOwnerMask;
                    item.GroupPermissions = grp.RootPart.GroupMask & grp.RootPart.NextOwnerMask;
                }
                else
                {
                    item.BasePermissions = grp.RootPart.BaseMask;
                    item.CurrentPermissions = grp.RootPart.OwnerMask;
                    item.NextPermissions = grp.RootPart.NextOwnerMask;
                    item.EveryOnePermissions = grp.RootPart.EveryoneMask;
                    item.GroupPermissions = grp.RootPart.GroupMask;
                }
                item.CreationDate = Util.UnixTimeSinceEpoch();

                // sets itemID so client can show item as 'attached' in inventory
                grp.SetFromItemID(item.ID);

                if (AddInventoryItem(item))
                    remoteClient.SendInventoryItemCreateUpdate(item, 0);
                else
                {
                    IDialogModule module = RequestModuleInterface<IDialogModule>();
                    if (module != null)
                        module.SendAlertToUser(remoteClient, "Operation failed");
                }

                itemID = item.ID;
                return item.AssetID;
            }
            return UUID.Zero;
        }

        /// <summary>
        /// Event Handler Rez an object into a scene
        /// Calls the non-void event handler
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="itemID"></param>
        /// <param name="RayEnd"></param>
        /// <param name="RayStart"></param>
        /// <param name="RayTargetID"></param>
        /// <param name="BypassRayCast"></param>
        /// <param name="RayEndIsIntersection"></param>
        /// <param name="EveryoneMask"></param>
        /// <param name="GroupMask"></param>
        /// <param name="RezSelected"></param>
        /// <param name="RemoveItem"></param>
        /// <param name="fromTaskID"></param>
        public virtual void RezObject(IClientAPI remoteClient, UUID itemID, Vector3 RayEnd, Vector3 RayStart,
                                    UUID RayTargetID, byte BypassRayCast, bool RayEndIsIntersection,
                                    bool RezSelected, bool RemoveItem, UUID fromTaskID)
        {
            IInventoryAccessModule invAccess = RequestModuleInterface<IInventoryAccessModule>();
            if (invAccess != null)
                invAccess.RezObject(
                    remoteClient, itemID, RayEnd, RayStart, RayTargetID, BypassRayCast, RayEndIsIntersection,
                    RezSelected, RemoveItem, fromTaskID, false);
        }
        
        /// <summary>
        /// Rez an object into the scene from a prim's inventory.
        /// </summary>
        /// <param name="sourcePart"></param>
        /// <param name="item"></param>
        /// <param name="pos"></param>
        /// <param name="rot"></param>
        /// <param name="vel"></param>
        /// <param name="param"></param>
        /// <returns>The SceneObjectGroup rezzed or null if rez was unsuccessful</returns>
        public virtual SceneObjectGroup RezObject(
            SceneObjectPart sourcePart, TaskInventoryItem item,
            Vector3 pos, Quaternion rot, Vector3 vel, int param, UUID RezzedFrom, bool RezObjectAtRoot)
        {
            if (item != null)
            {
                UUID ownerID = item.OwnerID;

                AssetBase rezAsset = AssetService.Get(item.AssetID.ToString());

                if (rezAsset != null)
                {
                    string xmlData = Utils.BytesToString(rezAsset.Data);
                    SceneObjectGroup group = SceneObjectSerializer.FromOriginalXmlFormat(xmlData, this);
                    if (group == null)
                        return null;

                    group.IsDeleted = false;
                    group.m_isLoaded = true;
                    foreach (SceneObjectPart part in group.ChildrenList)
                    {
                        part.IsLoading = false;
                    }
                    string reason;
                    if (!Permissions.CanRezObject(group.ChildrenList.Count, ownerID, pos, out reason))
                    {
                        GetScenePresence(ownerID).ControllingClient.SendAlertMessage("You do not have permission to rez objects here: " + reason);
                        return null;
                    }

                    AddPrimToScene(group);

                    SceneObjectPart rootPart = group.GetChildPart(group.UUID);
                    List<SceneObjectPart> partList = new List<SceneObjectPart>(group.ChildrenList);

                    // we set it's position in world.
                    // llRezObject sets the whole group at the position, while llRezAtRoot rezzes the group based on the root prim's position
                    // See: http://lslwiki.net/lslwiki/wakka.php?wakka=llRezAtRoot
                    // Shorthand: llRezAtRoot rezzes the root prim of the group at the position
                    //            llRezObject rezzes the center of group at the position
                    if (RezObjectAtRoot)
                        //This sets it right...
                        group.AbsolutePosition = pos;
                    else
                    {
                        //Find the 'center' of the group
                        //  Note: In SL, this is based on max - min
                        Vector3 MinPos = new Vector3(100000, 100000, 100000);
                        Vector3 MaxPos = Vector3.Zero;
                        foreach (SceneObjectPart child in partList)
                        {
                            if (child.AbsolutePosition.X < MinPos.X)
                                MinPos.X = child.AbsolutePosition.X;
                            if (child.AbsolutePosition.Y < MinPos.Y)
                                MinPos.Y = child.AbsolutePosition.Y;
                            if (child.AbsolutePosition.Z < MinPos.Z)
                                MinPos.Z = child.AbsolutePosition.Z;

                            if (child.AbsolutePosition.X > MaxPos.X)
                                MaxPos.X = child.AbsolutePosition.X;
                            if (child.AbsolutePosition.Y > MaxPos.Y)
                                MaxPos.Y = child.AbsolutePosition.Y;
                            if (child.AbsolutePosition.Z > MaxPos.Z)
                                MaxPos.Z = child.AbsolutePosition.Z;
                        }
                        Vector3 GroupAvg = ((MaxPos + MinPos) / 2);
                        Vector3 offset = group.AbsolutePosition - GroupAvg;
                        offset += pos;
                        group.AbsolutePosition = offset;
                    }

                    // Since renaming the item in the inventory does not affect the name stored
                    // in the serialization, transfer the correct name from the inventory to the
                    // object itself before we rez.
                    rootPart.Name = item.Name;
                    rootPart.Description = item.Description;

                    
                    group.SetGroup(sourcePart.GroupID, null);

                    if (rootPart.OwnerID != item.OwnerID)
                    {
                        if (Permissions.PropagatePermissions())
                        {
                            if ((item.CurrentPermissions & 8) != 0)
                            {
                                foreach (SceneObjectPart part in partList)
                                {
                                    part.EveryoneMask = item.EveryonePermissions;
                                    part.NextOwnerMask = item.NextPermissions;
                                }
                            }
                            group.ApplyNextOwnerPermissions();
                        }
                    }

                    foreach (SceneObjectPart part in partList)
                    {
                        if (part.OwnerID != item.OwnerID)
                        {
                            part.LastOwnerID = part.OwnerID;
                            part.OwnerID = item.OwnerID;
                            part.Inventory.ChangeInventoryOwner(item.OwnerID);
                        }
                        else if ((item.CurrentPermissions & 8) != 0) // Slam!
                        {
                            part.EveryoneMask = item.EveryonePermissions;
                            part.NextOwnerMask = item.NextPermissions;
                        }
                    }
                    
                    rootPart.TrimPermissions();
                    
                    if (group.RootPart.Shape.PCode == (byte)PCode.Prim)
                    {
                        group.ClearPartAttachmentData();
                    }
                    
                    group.UpdateGroupRotationR(rot);
                    
                    //group.ApplyPhysics(m_physicalPrim);
                    if (group.RootPart.PhysActor != null && group.RootPart.PhysActor.IsPhysical && vel != Vector3.Zero)
                    {
                        group.RootPart.ApplyImpulse((vel * group.GetMass()), false);
                        group.Velocity = vel;
                    }
                    group.CreateScriptInstances(param, true, DefaultScriptEngine, 2, RezzedFrom);

                    if (!Permissions.BypassPermissions())
                    {
                        if ((item.CurrentPermissions & (uint)PermissionMask.Copy) == 0)
                            sourcePart.Inventory.RemoveInventoryItem(item.ItemID);
                    }

                    group.ScheduleGroupForFullUpdate(PrimUpdateFlags.FullUpdate);
                   
                    return rootPart.ParentGroup;
                }
            }

            return null;
        }

        public virtual bool returnObjects(SceneObjectGroup[] returnobjects,
                UUID AgentId)
        {
            //AddReturns(returnobjects[0].OwnerID, returnobjects[0].Name, returnobjects.Length, returnobjects[0].AbsolutePosition, "parcel owner return");
            List<uint> IDs = new List<uint>();
            foreach (SceneObjectGroup grp in returnobjects)
            {
                IDs.Add(grp.LocalId);
            }
            IClientAPI client;
            TryGetClient(AgentId, out client);
            //Its ok if the client is null, its taken care of
            DeRezObjects(client, IDs, returnobjects[0].RootPart.GroupID, DeRezAction.Return, UUID.Zero);
            return true;
        }

        public void SetScriptRunning(IClientAPI controllingClient, UUID objectID, UUID itemID, bool running)
        {
            SceneObjectPart part = GetSceneObjectPart(objectID);
            if (part == null)
                return;

            if (running)
                EventManager.TriggerStartScript(part.LocalId, itemID);
            else
                EventManager.TriggerStopScript(part.LocalId, itemID);
        }

        public void GetScriptRunning(IClientAPI controllingClient, UUID objectID, UUID itemID)
        {
            EventManager.TriggerGetScriptRunning(controllingClient, objectID, itemID);
        }

        void ObjectOwner(IClientAPI remoteClient, UUID ownerID, UUID groupID, List<uint> localIDs)
        {
            if (!Permissions.IsGod(remoteClient.AgentId))
            {
                if (ownerID != UUID.Zero)
                    return;
                
                if (!Permissions.CanDeedObject(remoteClient.AgentId, groupID))
                    return;
            }

            List<SceneObjectGroup> groups = new List<SceneObjectGroup>();

            foreach (uint localID in localIDs)
            {
                SceneObjectPart part = GetSceneObjectPart(localID);
                if (!groups.Contains(part.ParentGroup))
                    groups.Add(part.ParentGroup);
            }

            foreach (SceneObjectGroup sog in groups)
            {
                if (ownerID != UUID.Zero)
                {
                    sog.SetOwnerId(ownerID);
                    sog.SetGroup(groupID, remoteClient);
                    sog.ScheduleGroupForFullUpdate(PrimUpdateFlags.FullUpdate);

                    foreach (SceneObjectPart child in sog.ChildrenList)
                        child.Inventory.ChangeInventoryOwner(ownerID);
                }
                else
                {
                    if (!Permissions.CanEditObject(sog.UUID, remoteClient.AgentId))
                        continue;

                    if (sog.GroupID != groupID)
                        continue;

                    foreach (SceneObjectPart child in sog.ChildrenList)
                    {
                        child.LastOwnerID = child.OwnerID;
                        child.Inventory.ChangeInventoryOwner(groupID);
                    }

                    sog.SetOwnerId(groupID);
                    sog.ApplyNextOwnerPermissions();
                }
            }

            foreach (uint localID in localIDs)
            {
                SceneObjectPart part = GetSceneObjectPart(localID);
                part.GetProperties(remoteClient);
            }
        }

        public void DelinkObjects(List<uint> primIds, IClientAPI client)
        {
            List<SceneObjectPart> parts = new List<SceneObjectPart>();

            foreach (uint localID in primIds)
            {
                SceneObjectPart part = GetSceneObjectPart(localID);

                if (part == null)
                    continue;

                if (Permissions.CanDelinkObject(client.AgentId, part.ParentGroup.RootPart.UUID))
                    parts.Add(part);
            }

            m_sceneGraph.DelinkObjects(parts);
        }

        public void LinkObjects(IClientAPI client, uint parentPrimId, List<uint> childPrimIds)
        {
            List<UUID> owners = new List<UUID>();

            List<SceneObjectPart> children = new List<SceneObjectPart>();
            SceneObjectPart root = GetSceneObjectPart(parentPrimId);

            if (root == null)
            {
                m_log.DebugFormat("[LINK]: Can't find linkset root prim {0}", parentPrimId);
                return;
            }

            if (!Permissions.CanLinkObject(client.AgentId, root.ParentGroup.RootPart.UUID))
            {
                m_log.DebugFormat("[LINK]: Refusing link. No permissions on root prim");
                return;
            }

            foreach (uint localID in childPrimIds)
            {
                SceneObjectPart part = GetSceneObjectPart(localID);

                if (part == null)
                    continue;

                if (!owners.Contains(part.OwnerID))
                    owners.Add(part.OwnerID);

                if (Permissions.CanLinkObject(client.AgentId, part.ParentGroup.RootPart.UUID))
                    children.Add(part);
            }

            // Must be all one owner
            //
            if (owners.Count > 1)
            {
                m_log.DebugFormat("[LINK]: Refusing link. Too many owners");
                client.SendAlertMessage("Permissions: Cannot link, too many owners.");
                return;
            }

            if (children.Count == 0)
            {
                m_log.DebugFormat("[LINK]: Refusing link. No permissions to link any of the children");
                client.SendAlertMessage("Permissions: Cannot link, not enough permissions.");
                return;
            }
            int LinkCount = 0;
            foreach(SceneObjectPart part in children)
            {
                LinkCount += part.ParentGroup.ChildrenList.Count;
            }

            IOpenRegionSettingsModule module = this.RequestModuleInterface<IOpenRegionSettingsModule>();
            if(module != null)
            {
                if (LinkCount > module.MaximumLinkCount &&
                    module.MaximumLinkCount != -1)
                {
                    client.SendAlertMessage("You cannot link more than " + module.MaximumLinkCount + " prims. Please try again with fewer prims.");
                    return;
                }
                if ((root.Flags & PrimFlags.Physics) == PrimFlags.Physics)
                {
                    //We only check the root here because if the root is physical, it will be applied to all during the link
                    if (LinkCount > module.MaximumLinkCountPhys &&
                        module.MaximumLinkCountPhys != -1)
                    {
                        client.SendAlertMessage("You cannot link more than " + module.MaximumLinkCountPhys + " physical prims. Please try again with fewer prims.");
                        return;
                    }
                }
            }

            m_sceneGraph.LinkObjects(root, children);
        }
    }
}
