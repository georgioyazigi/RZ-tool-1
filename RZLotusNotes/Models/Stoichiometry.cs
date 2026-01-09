// Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
using System.Collections.Generic;

namespace stoich
{
    public class Attributes
    {
        public List<Reactant> reactants { get; set; }
        public List<Product> products { get; set; }
        public string type { get; set; }
        public string eid { get; set; }
        public string name { get; set; }
        public string id { get; set; }
    }

    public class ColumnDefinitions
    {
        public List<ColDef> reactants { get; set; }
        public List<ColDef> products { get; set; }
        public List<ColDef> conditions { get; set; }
        public List<ColDef> solvents { get; set; }
        public string id { get; set; }
    }



    public class ColDef
    {
        public string key { get; set; }
        public string title { get; set; }
        public string type { get; set; }
    }


    public class Data
    {
        public string type { get; set; }
        public string id { get; set; }
        public Attributes attributes { get; set; }
    }

    public class Description
    {
        public string value { get; set; }
    }


    public class Included
    {
        public string type { get; set; }
        public string id { get; set; }
        public ColumnDefinitions attributes { get; set; }
    }

    public class Product
    {
        public string rxnid { get; set; }
        public string name { get; set; }
        public string mf { get; set; }
        public string productId { get; set; }
        public string row_id { get; set; }
    }

    public class Reactant
    {
        public string rxnid { get; set; }
        public string name { get; set; }
        public string mf { get; set; }
        public string row_id { get; set; }

    }

    public class Stoich
    {

        public Data data { get; set; }
        public List<Included> included { get; set; }
    }



}