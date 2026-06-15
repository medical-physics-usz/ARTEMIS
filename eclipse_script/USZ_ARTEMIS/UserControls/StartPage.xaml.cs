using System;
using VMS.TPS.Common.Model.API;

namespace USZ_ARTEMIS
{
    public partial class StartPage : System.Windows.Controls.UserControl
    {
        private Patient patient;
        private ScriptContext context;
        public bool enableOverride = true;
        public string couchModel = "";
        private bool clinicalVersion;
        public Nullable<double> ptvRingWidth = null;
        public Nullable<double> ptvRingGap = null;
        public Nullable<double> sibGapWidth = null;

        public StartPage(ScriptContext context, bool clinicalVersion = true)
        {
            InitializeComponent();
            this.context = context;
            patient = context.Patient;
            this.clinicalVersion = clinicalVersion;
            PopulateStructureSets();
        }
    }
}
