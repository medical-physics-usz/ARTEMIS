using System.Collections.Generic;
using System.Text;
using VMS.TPS.Common.Model.API;
using Image = VMS.TPS.Common.Model.API.Image;


namespace USZ_ARTEMIS.Tools
{
    class Actions
    {

        public static Course CreateQaCourse(ScriptContext context, Course selectedCourse)
        {
            // Create the QA course for the sCT

            // Get the course number to then create a new QA course
            string courseNumber = "";
            if (selectedCourse.Id.Length >= 2)
            {
                courseNumber = selectedCourse.Id.Substring(0, 2);
                if (courseNumber.EndsWith("_"))
                {
                    courseNumber = selectedCourse.Id.Substring(0, 1);
                }
            }

            // Intended ID of the new course and check if it exists
            string qaCourseId = courseNumber + "_sCT_QA";
            bool qaCourseAlreadyExists = false;

            IEnumerable<Course> allCourses = context.Patient.Courses;
            foreach (Course tempCourse in allCourses)
            {
                if (tempCourse.Id.Equals(qaCourseId))
                {
                    qaCourseAlreadyExists = true;
                }
            }

            // Create QA course
            Course qaCourse = null;

            if (!qaCourseAlreadyExists)
            {
                // create it if it does not exist
                qaCourse = context.Patient.AddCourse();
                qaCourse.Id = qaCourseId;

            }
            else
            {
                // get it if it already exists
                foreach (Course tempCourse in allCourses)
                {
                    if (tempCourse.Id.Equals(qaCourseId))
                    {
                        qaCourse = tempCourse;
                        //System.Windows.Forms.MessageBox.Show("it already exists");  // used to debug
                    }
                }
            }

            // Return the QA course
            return qaCourse;

        }

        public static PlanSetup FindCopyOriginalPlan(ScriptContext context, Course selectedCourse, PlanSetup selectedPlan)
        {

            //PlanSetup CopyOriginalPlan = null;
            Course qaCourse = USZ_ARTEMIS.Tools.Actions.CreateQaCourse(context, selectedCourse);
            string OriginalPlanID = selectedPlan.Id;


            IEnumerable<PlanSetup> allPlans = qaCourse.PlanSetups;
            foreach (PlanSetup tempPlan in allPlans)
            {
                if (tempPlan.Id.Equals(OriginalPlanID))
                {
                    return tempPlan;
                }
                //else
                //{
                //    System.Windows.Forms.MessageBox.Show("No Copy of the original plan found");
                //    return null;
                //}

            }
            return null;

        }

        public static bool CheckIfCopyOriginalPlan(ScriptContext context, Course selectedCourse, PlanSetup selectedPlan)
        {
            Course qaCourse = USZ_ARTEMIS.Tools.Actions.CreateQaCourse(context, selectedCourse);
            string OriginalPlanID = selectedPlan.Id;

            bool CheckIfCopyOriginalPlan = false;

            //is there a copy of original plan in qa course
            IEnumerable<PlanSetup> allPlans = qaCourse.PlanSetups;
            foreach (PlanSetup tempPlan in allPlans)
            {
                if (tempPlan.Id.Equals(OriginalPlanID))
                {
                    CheckIfCopyOriginalPlan = true;
                }
            }
            return CheckIfCopyOriginalPlan;
        }

        public static StructureSet CreateQaStructureSet(ScriptContext context, PlanSetup selectedPlan, string prefixID)
        {
            // Duplicate structure set from selected plan

            StructureSet currentStructureSet = selectedPlan.StructureSet;

            // Get the structure set date
            string structureSetDate = "";
            if (currentStructureSet.Id.Length >= 8)
            {
                //structureSetDate = currentStructureSet.Id.Substring(currentStructureSet.Id.Length - 8, 8);
                //// additional option if the date has only one digit
                //if (structureSetDate.StartsWith("_"))
                //{
                //    structureSetDate = structureSetDate.Substring(1);
                //}

                structureSetDate = currentStructureSet.Id.Substring(2); //position of the _
                if (structureSetDate.StartsWith("_"))
                {
                    structureSetDate = structureSetDate.Substring(0);
                }
                else
                {
                    structureSetDate = structureSetDate.Substring(1);
                }
            }


            // Intended ID of the new structure set and check if it exists
            if (prefixID.Length < 1 || prefixID.Length > 4)
            {
                // assign a standard if too long/short
                prefixID = "QA";
            }

            string qaStructureSetId = prefixID + structureSetDate;
            bool qaStrsAlreadyExists = false;

            IEnumerable<StructureSet> allStrs = context.Patient.StructureSets;
            foreach (StructureSet tempStrs in allStrs)
            {
                if (tempStrs.Id.Equals(qaStructureSetId))
                {
                    qaStrsAlreadyExists = true;
                }
            }

            // Create QA Structure set
            StructureSet qaStructureSet = null;

            if (!qaStrsAlreadyExists)
            {
                IEnumerable<Structure> allStructures = currentStructureSet.Structures;
                // Find the body
                Structure strBody = null;
                bool bodyFound = false;
                foreach (Structure tempStr in allStructures)
                {
                    if (tempStr.DicomType.Equals("EXTERNAL")) // body has strcutre type EXTERNAL
                    {
                        strBody = tempStr;
                        bodyFound = true;
                    }
                }
                // If found, check if is approved

                if (bodyFound && strBody.IsApproved)
                {
                    //System.Windows.Forms.MessageBox.Show("WARNING the BODY is approved. " +
                    //    "Please un-approve body before launching the script");
                    //return null; //interrupt execution of the method

                    qaStructureSet = currentStructureSet.Copy();
                    qaStructureSet.Id = qaStructureSetId;
                    qaStructureSet.Image.Id = qaStructureSetId;

                }
                else
                {
                    qaStructureSet = currentStructureSet.Copy();
                    qaStructureSet.Id = qaStructureSetId;
                    qaStructureSet.Image.Id = qaStructureSetId;
                }
            }
            else
            {
                // get it if it already exists
                foreach (StructureSet tempStrs in allStrs)
                {
                    if (tempStrs.Id.Equals(qaStructureSetId))
                    {
                        qaStructureSet = tempStrs;
                        qaStructureSet.Image.Id = qaStructureSetId;
                        //System.Windows.Forms.MessageBox.Show("it already exists");  // used to debug
                    }
                }

            }

            if (!qaStrsAlreadyExists)
            {
                // Rename related 3D image with the same ID

                // Intended ID of the new image and check if it exists
                string qaImageId = prefixID + structureSetDate;
                bool qaImageIdExists = false;

                IEnumerable<Image> allImages = selectedPlan.StructureSet.Image.Series.Images;
                foreach (Image tempImage in allImages)
                {
                    if (tempImage.Id.Equals(qaImageId))
                    {
                        qaImageIdExists = true;
                    }
                }

                // Rename it

                if (!qaImageIdExists)
                {
                    // rename it
                    qaStructureSet.Image.Id = qaImageId;

                }
                else
                {
                    // get it if it already exists
                    // keep the eclipse-created name
                }
            }



            return qaStructureSet;


        }


        public static PlanSetup CreateQaPlan(ScriptContext context, Course selectedCourse, PlanSetup selectedPlan, string prefixID)
        {

            Course qaCourse = USZ_ARTEMIS.Tools.Actions.CreateQaCourse(context, selectedCourse);
            StructureSet qaStructureSet = USZ_ARTEMIS.Tools.Actions.CreateQaStructureSet(context, selectedPlan, prefixID);

            PlanSetup CopyOriginalPlan = USZ_ARTEMIS.Tools.Actions.FindCopyOriginalPlan(context, selectedCourse, selectedPlan);

            // Copy the selected plan to a new structure set
            StringBuilder outMessage = new StringBuilder("null");

            // Intended ID of the new plan and check if it exists

            // Intended ID of the new structure set and check if it exists
            if (prefixID.Length < 1 || prefixID.Length > 4)
            {
                // assign a standard if too long/short
                prefixID = "QA";
            }

            string qaPlanId = prefixID;
            if (selectedPlan.Id.Length >= 3)
            {
                qaPlanId = prefixID + selectedPlan.Id.Substring(2);
            }

            bool qaPlanAlreadyExists = false;

            IEnumerable<PlanSetup> allPlans = qaCourse.PlanSetups;
            foreach (PlanSetup tempPlan in allPlans)
            {
                if (tempPlan.Id.Equals(qaPlanId))
                {
                    qaPlanAlreadyExists = true;
                }
            }

            // Create QA Plan
            PlanSetup qaPlan = null;

            //PlanSetup FindCopyOriginalPlan = USZ_ARTEMIS.Tools.Actions.FindCopyOriginalPlan(context, selectedCourse, selectedPlan);
            if (!qaPlanAlreadyExists)
            {
                // create it if it does not exist
                qaPlan = qaCourse.CopyPlanSetup(CopyOriginalPlan, qaStructureSet, outMessage);
                qaPlan.Id = qaPlanId;
                if (qaCourse.Id.Length >= 2) { qaPlan.Name = qaCourse.Id.Substring(0, 2) + qaPlanId; }
                // System.Windows.Forms.MessageBox.Show(outMessage.ToString());  // used to debug

            }
            else
            {
                // get it if it already exists
                foreach (PlanSetup tempPlan in allPlans)
                {
                    if (tempPlan.Id.Equals(qaPlanId))
                    {
                        qaPlan = tempPlan;
                    }
                }

            }


            return qaPlan;
        }


        public static void RecalculatePlan(ScriptContext context, PlanSetup originalPlan, PlanSetup qaPlan)
        {

            PlanSetup selectedPlan = originalPlan;
            Course selectedCourse = selectedPlan.Course;

            Course qaCourse = USZ_ARTEMIS.Tools.Actions.CreateQaCourse(context, selectedCourse);
            //PlanSetup CopyOriginalPlan = USZ_ARTEMIS.Tools.Actions.FindCopyOriginalPlan(context, selectedCourse, selectedPlan);

            // Find the external plan corresponding to the qa plan

            IEnumerable<ExternalPlanSetup> allExtPlans = qaCourse.ExternalPlanSetups;
            ExternalPlanSetup qaExtPlan = null;
            foreach (ExternalPlanSetup tempExtPlan in allExtPlans)
            {
                if (tempExtPlan.Id.Equals(qaPlan.Id))
                {
                    qaExtPlan = tempExtPlan;
                }
            }

            // Check that there is dose on the original plan
            if (USZ_ARTEMIS.Tools.ExtractData.GetTotalMu(selectedPlan) > 0)
            { // Calculate dose
                // System.Windows.Forms.MessageBox.Show("Dose will recalculate, please be patient, no progress bar is shown");
                qaExtPlan.CalculateDose();

                // System.Windows.Forms.MessageBox.Show("Dose calculation done!");


                // Re-normalize the QA plan to have the same MU as the original plan
                double ratioMU = USZ_ARTEMIS.Tools.ExtractData.GetTotalMu(selectedPlan)
                    / USZ_ARTEMIS.Tools.ExtractData.GetTotalMu(qaPlan);
                qaExtPlan.PlanNormalizationValue /= ratioMU;

            }
            else
            {
                System.Windows.Forms.MessageBox.Show("No dose on original plan!\nPlease calculate dose on the original plan and re-run the script");
            }

        }


        public static Structure OverrideBody(ScriptContext context, Course selectedCourse, PlanSetup selectedPlan, string prefixID, Structure strBody)
        {
            PlanSetup qaPlanWater = USZ_ARTEMIS.Tools.Actions.CreateQaPlan(context, selectedCourse, selectedPlan, "QW");

            // Assign water to the body of this structure set 
            StructureSet qaStructureSetWater = qaPlanWater.StructureSet;
            IEnumerable<Structure> allStructures = qaStructureSetWater.Structures;

            // Find the body
            bool bodyFound = false;
            foreach (Structure tempStr in allStructures)
            {
                if (tempStr.DicomType.Equals("EXTERNAL")) // body has strcutre type EXTERNAL
                {
                    strBody = tempStr;
                    bodyFound = true;
                }
            }

            // If found, assign HU=0
            string errorMsg;
            if (bodyFound && strBody.CanSetAssignedHU(out errorMsg))
            {
                strBody.SetAssignedHU(0);
            }

            return strBody;
            // tested on 00950319
        }


        public static bool CheckIfRunFromOriginalPlan(ScriptContext context, Course selectedCourse, PlanSetup selectedPlan)
        {
            Course qaCourse = USZ_ARTEMIS.Tools.Actions.CreateQaCourse(context, selectedCourse);
            //string qaCourseID = qaCourse.Id;
            //string selectedCourseID = selectedPlan.Course.Id;

            bool CheckIfRunFromOriginalPlan = false;

            //is selected plan in the original course
            if (selectedCourse.Id.Contains("sCT_QA"))
            {
                CheckIfRunFromOriginalPlan = false;
            }
            else
            {
                CheckIfRunFromOriginalPlan = true;//it is in the original course
            }
            return CheckIfRunFromOriginalPlan;
        }

        public static void RenameCopyPlan(ScriptContext context, PlanSetup selectedPlan, Course selectedCourse)
        {

            PlanSetup CopyOriginalPlan = Tools.Actions.FindCopyOriginalPlan(context, selectedCourse, selectedPlan);
            if (CopyOriginalPlan != null)
            {
                string proposedNewId = "QQ" + CopyOriginalPlan.Id.Substring(2);
                bool proposedNewIdExists = false;

                foreach (PlanSetup tempPlan in CopyOriginalPlan.Course.PlanSetups)
                {
                    if (tempPlan.Id == proposedNewId) { proposedNewIdExists = true; }
                }

                if (proposedNewIdExists)
                {
                    System.Windows.Forms.MessageBox.Show("Please delete the plan with ID = " + proposedNewId + " in the course " + CopyOriginalPlan.Course.Id + " and re-run the script");  // in case the script is called multiple times
                }
                else
                {
                    CopyOriginalPlan.Id = proposedNewId;
                }

            }
        }

    }
}
