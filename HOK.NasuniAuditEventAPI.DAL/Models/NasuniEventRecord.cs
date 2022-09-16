namespace HOK.NasuniAuditEventAPI.DAL.Models
{
    public class NasuniEventRecord
    {
        //{"MessageSourceAddress":"192.168.1.2","event_type":"AUDIT_RENAME","is_dir":true,"size":null,"timestamp":1593437724,"filesize":null,"username":"Contoso\\a.person","path_timestamp":0,"path":"/now/testfolder/","newpath":"/now/testfoldernew/","resource":"CONTOSO","name":null}
        public event_types event_type { get; set; }
        public bool is_dir { get; set; }
        private string _path;
        private string _newpath;
        /// <summary>
        /// lower case and '\' path conversion on get
        /// </summary>
        public string path
        {
            get { return _path; }
            set { _path = value?.Replace('/', '\\').ToLowerInvariant(); }
        }
        /// <summary>
        /// lower case and '\' path conversion on get
        /// </summary>
        public string newpath
        {
            get { return _newpath; }
            set { _newpath = value?.Replace('/', '\\').ToLowerInvariant(); }
        }
        public int timestamp { get; set; }//javascript timestamp. Need to verify if unix timestamp needs dividing by 1000 to work on windows...javascriptconvert doesn't seem to work directly.
        public string username { get; set; }//we care about username as I think only 'real' intentional acl changes are made by an actual user (not the machine/system account)
        public enum event_types
        {
            AUDIT_RENAME,//renames associated with windows explorer new folder/file creation
            AUDIT_SETXATTR,//associated with ACL changes and other events
            AUDIT_WRITE,//writes
            AUDIT_UNLINK,//associated with delete
            //below are events we don't care about but include incase they aren't prefiltered by audit logging system
            AUDIT_MKNOD,
            AUDIT_CHOWN,
            AUDIT_LISTXATTR,
            AUDIT_READDIR,
            AUDIT_READ
        }
    }
}
