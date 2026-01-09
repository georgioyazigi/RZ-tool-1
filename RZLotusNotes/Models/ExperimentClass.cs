using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VUW_move.Models
{
    public class ActualAmount
    {
        public string amount_value { get; set; }
        public string amount_unit { get; set; }
        public string calcmoles_value { get; set; }
        public string calcmoles_unit { get; set; }
        public string calcmass_value { get; set; }
        public string calcmass_unit { get; set; }
        public string calcvol_value { get; set; }
        public string calcvol_unit { get; set; }
        public string calcequiv { get; set; }
        public string theoryact_value { get; set; }
        public string theoryact_unit { get; set; }
        public string yield { get; set; }
    }


    public class Metadata
    {

        public string containerid { get; set; }
        public string autoname { get; set; }
        public string vaultpath { get; set; }
        public string versioncreationdate { get; set; }
        public string signals_notebook { get; set; }
        public string attributeList_fundingSource { get; set; }
        public string attributeList_researchStakeholder { get; set; }
        public string attributeList_project { get; set; }
        public string author { get; set; }
        // public string institute { get; set; }
        // public string site { get; set; }
        //public string experimentnumber { get; set; }
        public string createdate { get; set; }

        public string description { get; set; }

        // public string projectname { get; set; }
        public string projectid { get; set; }
        // public string client { get; set; }
        public string projectmanager { get; set; }
        // public string summaryobjective { get; set; }
        public string keywords { get; set; }
        public string eid { get; set; }
        public string completed { get; set; }

    }

    public class Material
    {
        public string name { get; set; }
        public string density_value { get; set; }
        public string density_unit { get; set; }
        public string role { get; set; }
        public string mw { get; set; }
        public string mf { get; set; }
        public string casnumber { get; set; }
        public string label { get; set; }
        public string limitingreagent { get; set; }
        public string diluent { get; set; }
        public string purityconcentration_value { get; set; }
        public string purityconcentration_unit { get; set; }
        public string stoichiometriccoefficient { get; set; }

        [JsonProperty("planned-amount")]
        public PlannedAmount plannedamount { get; set; }

        [JsonProperty("actual-amount")]
        public ActualAmount actualamount { get; set; }
    }

    public class OtherPages
    {
        public List<string> File { get; set; }

        [JsonProperty("Text Content")]
        public List<string> TextContent { get; set; }
        public List<string> File_1 { get; set; }
        public List<string> NMR { get; set; }
        public List<string> HRMS { get; set; }
        public List<string> Procedure { get; set; }
        public List<string> Spreadsheet { get; set; }
        public List<string> Literature { get; set; }

        [JsonProperty("flash column chromatography")]
        public List<string> flashcolumnchromatography { get; set; }
        public List<string> HPLC_CAD { get; set; }

        [JsonProperty("NMR of starting material")]
        public List<string> NMRofstartingmaterial { get; set; }
        public List<string> LCMS { get; set; }
        public List<string> LRMS { get; set; }

        [JsonProperty("Flash column chromatography")]
        public List<string> Flashcolumnchromatography { get; set; }

        public List<string> LC { get; set; }

        [JsonProperty("Mass Spec")]
        public List<string> MassSpec { get; set; }

        [JsonProperty("SnapGene file")]
        public List<string> SnapGenefile { get; set; }
        public List<string> PCR { get; set; }

        [JsonProperty("III, IV")]
        public List<string> IIIIV { get; set; }
        public List<string> Chromatography { get; set; }

        [JsonProperty("Chiral HPLC")]
        public List<string> ChiralHPLC { get; set; }
        public List<string> HPLCs { get; set; }
        public List<string> MS { get; set; }

    }
    public class Experiment
    {
        public Metadata Background { get; set; }
        public Reactions Reactions { get; set; }
        public Stoichiometry Stoichiometry { get; set; }
        public OtherPages otherPages { get; set; }
    }
    public class Reactions
    {
        [JsonProperty("Step 1")]
        public string Step1 { get; set; }

        [JsonProperty("Step 2")]
        public string Step2 { get; set; }

        [JsonProperty("Step 3")]
        public string Step3 { get; set; }
        [JsonProperty("Step 4")]
        public string Step4 { get; set; }
        [JsonProperty("Step 5")]
        public string Step5 { get; set; }
    }
    public class Stoichiometry
    {
        [JsonProperty("Step 1")]
        public Step step1 { get; set; }
        [JsonProperty("Step 2")]
        public Step step2 { get; set; }
        [JsonProperty("Step 3")]
        public Step step3 { get; set; }
        [JsonProperty("Step 4")]
        public Step step4 { get; set; }
        [JsonProperty("Step 5")]
        public Step step5 { get; set; }
    }

    public class Step
    {
        public List<Material> materials { get; set; }
        public string reaction { get; set; }
    }

    public class PlannedAmount
    {
        public string calcequiv { get; set; }
        public string amount_value { get; set; }
        public string amount_unit { get; set; }
        public string calcmass_value { get; set; }
        public string calcmass_unit { get; set; }
        public string calcvolume_value { get; set; }
        public string calcvolume_unit { get; set; }
        public string calcmoles_value { get; set; }
        public string calcmoles_unit { get; set; }
    }
}
