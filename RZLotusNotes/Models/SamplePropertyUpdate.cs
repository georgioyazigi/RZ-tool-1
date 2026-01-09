using System.Collections.Generic;

public class SamplePropertyUpdate
{
    public TopData data { get; set; }
    public SamplePropertyUpdate()
    {
        data = new TopData();
        data.attributes = new TopDataAttributes();
        data.attributes.data = new List<PropertiesToUpdate>();
    }
}
public class TopData
{
    public TopDataAttributes attributes { get; set; }
}
public class TopDataAttributes
{
    public List<PropertiesToUpdate> data { get; set; }

}
public class PropertiesToUpdate
{
    public PatchPropertyAttributes attributes { get; set; }
    public string id { get; set; }
    public string type { get; set; }
    public PropertiesToUpdate(string _id, string _value)
    {
        id = _id;
        type = "property";
        attributes = new PatchPropertyAttributes(_value);
    }
}
public class PatchPropertyAttributes
{

    public Content content { get; set; }
    public PatchPropertyAttributes(string _value)
    {
        content = new Content(_value);
    }
}



public class Content
{
    public string value { get; set; }
    public Content(string _value)
    {
        value = _value;
    }
}