using System;
using System.Collections.Generic;
using C5;

namespace Aurora.DataManager.Migration.Migrators
{
    public class AuroraMigrator_2010_03_13 : Migrator
    {
        public AuroraMigrator_2010_03_13()
        {
            Version = new Version(2010, 3, 13);
            CanProvideDefaults = true;

            schema = new List<Rec<string, ColumnDefinition[]>>();

            AddSchema("usernotes", ColDefs(
                ColDef("userid", ColumnTypes.String50),
                ColDef("targetuuid", ColumnTypes.String50),
                ColDef("notes", ColumnTypes.String512),
                ColDef("noteUUID", ColumnTypes.String50, true)
                ));

            AddSchema("userpicks", ColDefs(
                ColDef("pickuuid", ColumnTypes.String50, true),
                ColDef("creatoruuid", ColumnTypes.String50),
                ColDef("toppick", ColumnTypes.String512),
                ColDef("parceluuid", ColumnTypes.String50),
                ColDef("name", ColumnTypes.String50),
                ColDef("description", ColumnTypes.String50),
                ColDef("snapshotuuid", ColumnTypes.String50),
                ColDef("user", ColumnTypes.String50),
                ColDef("originalname", ColumnTypes.String50),
                ColDef("simname", ColumnTypes.String50),
                ColDef("posglobal", ColumnTypes.String50),
                ColDef("sortorder", ColumnTypes.String50),
                ColDef("enabled", ColumnTypes.String50)
                ));

            AddSchema("usersauth", ColDefs(
                ColDef("userUUID", ColumnTypes.String50, true),
                ColDef("userLogin", ColumnTypes.String50),
                ColDef("userFirst", ColumnTypes.String512),
                ColDef("userLast", ColumnTypes.String50),
                ColDef("userEmail", ColumnTypes.String50),
                ColDef("userPass", ColumnTypes.String50),
                ColDef("userMac", ColumnTypes.String50),
                ColDef("userIP", ColumnTypes.String50),
                ColDef("userAcceptTOS", ColumnTypes.String50),
                ColDef("userGodLevel", ColumnTypes.String50),
                ColDef("userRealFirst", ColumnTypes.String50),
                ColDef("userRealLast", ColumnTypes.String50),
                ColDef("userAddress", ColumnTypes.String50),
                ColDef("userZip", ColumnTypes.String50),
                ColDef("userCountry", ColumnTypes.String50),
                ColDef("tempBanned", ColumnTypes.String50),
                ColDef("permaBanned", ColumnTypes.String50),
                ColDef("profileAllowPublish", ColumnTypes.String50),
                ColDef("profileMaturePublish", ColumnTypes.String50),
                ColDef("profileURL", ColumnTypes.String50),
                ColDef("AboutText", ColumnTypes.String50),
                ColDef("Email", ColumnTypes.String50),
                ColDef("CustomType", ColumnTypes.String50),
                ColDef("profileWantToMask", ColumnTypes.String50),
                ColDef("profileWantToText", ColumnTypes.String50),
                ColDef("profileSkillsMask", ColumnTypes.String50),
                ColDef("profileSkillsText", ColumnTypes.String50),
                ColDef("profileLanguages", ColumnTypes.String50),
                ColDef("visible", ColumnTypes.String50),
                ColDef("imviaemail", ColumnTypes.String50),
                ColDef("membershipGroup", ColumnTypes.String50),
                ColDef("FirstLifeAboutText", ColumnTypes.String50),
                ColDef("FirstLifeImage", ColumnTypes.String50),
                ColDef("Partner", ColumnTypes.String50),
                ColDef("Image", ColumnTypes.String50),
                ColDef("AArchiveName", ColumnTypes.String50),
                ColDef("IsNewUser", ColumnTypes.String50)
                ));

            AddSchema("classifieds", ColDefs(
                ColDef("classifieduuid", ColumnTypes.String50, true),
                ColDef("creatoruuid", ColumnTypes.String50),
                ColDef("creationdate", ColumnTypes.String512),
                ColDef("expirationdate", ColumnTypes.String50),
                ColDef("category", ColumnTypes.String50),
                ColDef("name", ColumnTypes.String50),
                ColDef("description", ColumnTypes.String50),
                ColDef("parceluuid", ColumnTypes.String50),
                ColDef("parentestate", ColumnTypes.String50),
                ColDef("snapshotuuid", ColumnTypes.String50),
                ColDef("simname", ColumnTypes.String50),
                ColDef("posglobal", ColumnTypes.String50),
                ColDef("parcelname", ColumnTypes.String50),
                ColDef("classifiedflags", ColumnTypes.String50),
                ColDef("priceforlisting", ColumnTypes.String50)
                ));
            
            AddSchema("auroraregions", ColDefs(
                ColDef("regionName", ColumnTypes.String50),
                ColDef("regionHandle", ColumnTypes.String50),
                ColDef("hidden", ColumnTypes.String1),
                ColDef("regionUUID", ColumnTypes.String50, true),
                ColDef("regionX", ColumnTypes.String50),
                ColDef("regionY", ColumnTypes.String50),
                ColDef("telehubX", ColumnTypes.String50),
                ColDef("telehubY", ColumnTypes.String50)
                ));

            AddSchema("macban", ColDefs(ColDef("macAddress", ColumnTypes.String50, true)));

            AddSchema("BannedViewers", ColDefs(ColDef("Client", ColumnTypes.String50, true)));

            AddSchema("mutelists", ColDefs(
                ColDef("userID", ColumnTypes.String50),
                ColDef("muteID", ColumnTypes.String50),
                ColDef("muteName", ColumnTypes.String50),
                ColDef("muteType", ColumnTypes.String50),
                ColDef("muteUUID", ColumnTypes.String50, true)
                ));

            AddSchema("aurorainventoryfolders", ColDefs(
                ColDef("FolderUUID", ColumnTypes.String50, true),
                ColDef("parentID", ColumnTypes.String50),
                ColDef("PreferredAssetType", ColumnTypes.String50),
                ColDef("ParentFolder", ColumnTypes.String50),
                ColDef("Name", ColumnTypes.String50)
                ));
            
            AddSchema("abusereports", ColDefs(
                ColDef("Category", ColumnTypes.String100),
                ColDef("AReporter", ColumnTypes.String100),
                ColDef("OName", ColumnTypes.String100),
                ColDef("OUUID", ColumnTypes.String100),
                ColDef("AName", ColumnTypes.String100, true),
                ColDef("ADetails", ColumnTypes.String100),
                ColDef("OPos", ColumnTypes.String100),
                ColDef("Estate", ColumnTypes.String100),
                ColDef("Summary", ColumnTypes.String100)
                ));

            AddSchema("assetMediaURL", ColDefs(
                ColDef("objectUUID", ColumnTypes.String100, true),
                ColDef("User", ColumnTypes.String100),
                ColDef("alt_image_enable", ColumnTypes.String100),
                ColDef("auto_loop", ColumnTypes.String100),
                ColDef("auto_play", ColumnTypes.String100),
                ColDef("auto_scale", ColumnTypes.String100),
                ColDef("auto_zoom", ColumnTypes.String100),
                ColDef("controls", ColumnTypes.String100),
                ColDef("current_url", ColumnTypes.String100),
                ColDef("first_click_interact", ColumnTypes.String100),
                ColDef("height_pixels", ColumnTypes.String100),
                ColDef("home_url", ColumnTypes.String100),
                ColDef("perms_control", ColumnTypes.String100),
                ColDef("perms_interact", ColumnTypes.String100),
                ColDef("whitelist", ColumnTypes.String100),
                ColDef("whitelist_enable", ColumnTypes.String100),
                ColDef("width_pixels", ColumnTypes.String100),
                ColDef("object_media_version", ColumnTypes.String100, true),
                ColDef("count", ColumnTypes.String100)
                ));
            
            AddSchema("auroraprims", ColDefs(
                ColDef("primUUID", ColumnTypes.String45, true),
                ColDef("primName", ColumnTypes.String45),
                ColDef("primType", ColumnTypes.String2),
                ColDef("primKeys", ColumnTypes.String1024),
                ColDef("primValues", ColumnTypes.String1024)
                ));
        }

        protected override void DoCreateDefaults(DataSessionProvider sessionProvider, IGenericData genericData)
        {
            EnsureAllTablesInSchemaExist(genericData);
        }

        protected override bool DoValidate(DataSessionProvider sessionProvider, IGenericData genericData)
        {
            return TestThatAllTablesValidate(genericData);
        }

        protected override void DoMigrate(DataSessionProvider sessionProvider, IGenericData genericData)
        {
            DoCreateDefaults(sessionProvider, genericData);
        }

        protected override void DoPrepareRestorePoint(DataSessionProvider sessionProvider, IGenericData genericData)
        {
            CopyAllTablesToTempVersions(genericData);
        }

        public override void DoRestore(DataSessionProvider sessionProvider, IGenericData genericData)
        {
            RestoreTempTablesToReal(genericData);
        }
    }
}