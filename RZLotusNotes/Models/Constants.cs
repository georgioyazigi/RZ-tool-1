using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common.Models
{

    public static class Constants
    {
        public const string EntitiesEndpoint = "/api/rest/v1.0/entities/";

        public const string GetADOEndpoint = "?type=ado&include=untrashed,systemTemplate&depth=1";

        public const string AdtsEndpoint = "/api/rest/v1.0/adt/";

        public const string UploadMultipartEndpoint = "/api/multipart-upload/upload-part-data/";

        public const string AbortMultipartEndpoint = "/api/multipart-upload/abort-upload/";

        public const string StatusCompleted = "Completed";

        public const string StatusFailed = "Failed";
    }

}
