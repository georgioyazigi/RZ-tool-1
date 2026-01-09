using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;


namespace Common.Models
{
    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
    public class Attributes
    {
        public string id { get; set; }
        public string type { get; set; }
        public string name { get; set; }
        public int index { get; set; }
        public List<Entity> entities { get; set; }
        public string eid { get; set; }
        public string digest { get; set; }
        public Fields fields { get; set; }
    }

    public class Data
    {
        public string type { get; set; }
        public string id { get; set; }
        public Attributes attributes { get; set; }
        public Relationships relationships { get; set; }
    }

    public class Description
    {
        public string value { get; set; }
    }

    public class Entity
    {
        public string entityEid { get; set; }
        public string entityName { get; set; }
        public int index { get; set; }
    }

    public class Entity2
    {
        public Links links { get; set; }
        public Data data { get; set; }
    }

    public class Fields
    {
        public Description Description { get; set; }
        public Name Name { get; set; }
    }

    public class Included
    {
        public string type { get; set; }
        public string id { get; set; }
        public Links links { get; set; }
        public Attributes attributes { get; set; }
    }

    public class Links
    {
        public string self { get; set; }
    }

    public class Name
    {
        public string value { get; set; }
    }

    public class Relationships
    {
        public Entity entity { get; set; }
    }

    public class ExperimentLayout
    {
        public Links links { get; set; }
        public List<Data> data { get; set; }
        public List<Included> included { get; set; }
    }


}