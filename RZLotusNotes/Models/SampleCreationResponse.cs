


using System;

public class Attributes
{
    public string id { get; set; }
    public string eid { get; set; }
    public string name { get; set; }
    public string description { get; set; }
    public DateTime createdAt { get; set; }
    public DateTime editedAt { get; set; }
    public string type { get; set; }
    public string digest { get; set; }

}

public class Data
{
    public string type { get; set; }
    public string id { get; set; }

    public Attributes attributes { get; set; }

}



public class SampleCreationResponse
{

    public Data data { get; set; }

}





