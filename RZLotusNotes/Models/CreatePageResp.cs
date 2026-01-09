using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;


namespace Common.Models
{
    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
    public class AttributesC
    {
        public string id { get; set; }
        public string type { get; set; }
        public string name { get; set; }
        public int index { get; set; }
        public List<object> entities { get; set; }
        public string eid { get; set; }
        public string digest { get; set; }
        public FieldsC fields { get; set; }
    }

    public class DataC
    {
        public string type { get; set; }
        public string id { get; set; }
        public AttributesC attributes { get; set; }
        public RelationshipsC relationships { get; set; }
    }

    public class DescriptionC
    {
        public string value { get; set; }
    }

    public class EntityC
    {
        public LinksC links { get; set; }
        public DataC data { get; set; }
    }

    public class FieldsC
    {
        public DescriptionC Description { get; set; }
        public NameC Name { get; set; }
    }

    public class IncludedC
    {
        public string type { get; set; }
        public string id { get; set; }
        public LinksC links { get; set; }
        public AttributesC attributes { get; set; }
    }

    public class LinksC
    {
        public string self { get; set; }
    }

    public class NameC
    {
        public string value { get; set; }
    }

    public class RelationshipsC
    {
        public EntityC entity { get; set; }
    }

    public class CreatePageResp
    {
        public LinksC links { get; set; }
        public DataC data { get; set; }
        public List<IncludedC> included { get; set; }
    }



}