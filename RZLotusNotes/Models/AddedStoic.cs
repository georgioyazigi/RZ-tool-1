using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AddedStoich { public class Attributes { public string type { get; set; } public string id { get; set; } } public class Data { public string type { get; set; } public string id { get; set; } public Attributes attributes { get; set; } } public class Links { public string self { get; set; } } public class AddedStoich { public Links links { get; set; } public Data data { get; set; } } }