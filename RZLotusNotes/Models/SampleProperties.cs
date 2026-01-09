using System.Collections.Generic;

public class PropertyAttributes
{
    public string id { get; set; }
    public string name { get; set; }
    public string type { get; set; }


}

public class PropertyData
{
    public string type { get; set; }
    public string id { get; set; }

    public PropertyAttributes attributes { get; set; }
    public PropertyData()
    {
        attributes = new PropertyAttributes();
    }
}

public class SampleProperties
{

    public List<PropertyData> data { get; set; }
    public SampleProperties()
    {
        data = new List<PropertyData>();
    }

}
