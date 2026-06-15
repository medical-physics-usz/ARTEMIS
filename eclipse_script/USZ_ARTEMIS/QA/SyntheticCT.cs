using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
// using static System.Windows.Forms.VisualStyles.VisualStyleElement.ProgressBar;
using iTextSharp.text;
using iTextSharp.text.pdf;
using USZ_ARTEMIS.Configuration;
using USZ_ARTEMIS.Tools;
// using System.Windows.Documents;
using VMS.TPS.Common.Model.API;
using Font = iTextSharp.text.Font;

namespace USZ_ARTEMIS.QA
{
    internal class SyntheticCT
    {
        public static PlanSetup PreparePlan(ScriptContext context)
        {
            // Collect variables
            Patient patient = context.Patient;
            Course srcCourse = context.Course;
            PlanSetup srcPlan = context.PlanSetup;

            // Create the sCT QA course if needed
            Course qaCourse = USZ_ARTEMIS.Tools.Actions.CreateQaCourse(context, srcCourse);

            // Copy the orignal plan
            PlanSetup qaPlan = qaCourse.CopyPlanSetup(srcPlan);

            // Create the QA structure set
            StructureSet srcSS = patient.StructureSets.Single(st => st.Id == srcPlan.StructureSet.Id);
            StructureSet qaSS = srcSS.Image.CreateNewStructureSet();
            foreach (Structure srcStructure in srcSS.Structures)
            {
                Structure qaStructure = qaSS.AddStructure(srcStructure.DicomType, srcStructure.Id);
                qaStructure.SegmentVolume = srcStructure.SegmentVolume;
            }

            // Override body with water
            foreach (Structure structure in qaSS.Structures)
            {
                if (structure.DicomType.Equals("EXTERNAL")) // body has strcutre type EXTERNAL
                {
                    structure.SetAssignedHU(0);
                }
            }

            // Copy the original plan onto the new structure set
            string PlanId_New = "QW" + srcPlan.Id.Substring(2);
            StringBuilder outputDiagnstics = new StringBuilder("Type in the information about copied plan.");
            PlanSetup copiedPlan = qaCourse.CopyPlanSetup(srcPlan, qaSS, outputDiagnstics);
            copiedPlan.Id = PlanId_New;
            qaSS.Id = PlanId_New;

            return copiedPlan;

        }

        public static void RecalculatePlan(ScriptContext context, PlanSetup qaPlan)
        {
            PlanSetup srcPlan = context.PlanSetup;
            USZ_ARTEMIS.Tools.Actions.RecalculatePlan(context, srcPlan, qaPlan);

        }

        public static void CreatePDF(ScriptContext context, PlanSetup orgPlan, PlanSetup qaPlanQW)
        {
            //the script must me run on the original plan not the qa plan
            if (orgPlan.Id.StartsWith("QA_"))
            {
                System.Windows.Forms.MessageBox.Show("WARNING The selected plan is a QA plan. " +
                    "Please select the original plan to launch the script");
                return; //interrupt execution of the method
            }
            else
            {
            }

            string FileName = USZ_ARTEMIS.Tools.ExtractData.GetPatientID(context) + "_" + orgPlan.Course.Id + "_" + orgPlan.Id + ".pdf";
            string PathNamePdf = Path.Combine(AppPaths.ReportsPdfFolder, FileName);

            System.IO.FileStream fs = new FileStream(PathNamePdf, FileMode.Create);

            Document document = new Document(PageSize.A4, 25, 25, 30, 30);

            PdfWriter writer = PdfWriter.GetInstance(document, fs);


            // Open the document to enable you to write to the document 
            // TODO: warning if the PDF is oopened already
            document.Open();

            // Add a simple and wellknown phrase to the document in a flow layout manner

            // First page with basic information, always present

            string PatientID = USZ_ARTEMIS.Tools.ExtractData.GetPatientID(context);
            string PatientName = USZ_ARTEMIS.Tools.ExtractData.GetPatientName(context);
            string Oncologist = USZ_ARTEMIS.Tools.ExtractData.GetOncologist(context);
            string Physicist = USZ_ARTEMIS.Tools.ExtractData.GetPhysicist(context);


            string PlanCourse = USZ_ARTEMIS.Tools.ExtractData.GetCourse(context, orgPlan);
            string Plan = USZ_ARTEMIS.Tools.ExtractData.GetPlan(context, orgPlan);
            string StructureSet = USZ_ARTEMIS.Tools.ExtractData.GetStructureSet(context, orgPlan);
            string ImageDevice = USZ_ARTEMIS.Tools.ExtractData.GetImageDevice(context, orgPlan);
            string TreatmentMachine = USZ_ARTEMIS.Tools.ExtractData.GetTreatmentMachine(context, orgPlan);
            string PrescribedDose = USZ_ARTEMIS.Tools.ExtractData.GetPrescribedDose(context, orgPlan).ToString();
            string DosePerFraction = USZ_ARTEMIS.Tools.ExtractData.GetDosePerFraction(context, orgPlan);
            string NumberOfFractions = USZ_ARTEMIS.Tools.ExtractData.GetNumberOfFractions(context, orgPlan);
            string PTV = USZ_ARTEMIS.Tools.ExtractData.FindPTV(context, orgPlan).ToString();
            string Energy = USZ_ARTEMIS.Tools.ExtractData.GetEnergy(context, orgPlan);


            string TotalMU = USZ_ARTEMIS.Tools.ExtractData.GetTotalMu(orgPlan).ToString("0.000");
            string NormalizationValue = USZ_ARTEMIS.Tools.ExtractData.GetNormalizationValue(context, orgPlan);
            string NormalizationMode = USZ_ARTEMIS.Tools.ExtractData.GetNormalizationMode(context, orgPlan);

            DateTime today = DateTime.Now;

            //add title
            Font bold = new Font(Font.FontFamily.HELVETICA, 14, Font.BOLD);

            document.Add(new iTextSharp.text.Paragraph("                          REPORT PSQA DOSE CALCULATION FOR sCT", bold));

            //add info patient
            document.Add(new iTextSharp.text.Paragraph(" "));
            document.Add(new iTextSharp.text.Paragraph("Date : " + today.ToString()));
            document.Add(new iTextSharp.text.Paragraph("Patient ID : " + PatientID));
            document.Add(new iTextSharp.text.Paragraph("Patient Name : " + PatientName));
            document.Add(new iTextSharp.text.Paragraph("Oncologist : " + Oncologist));
            document.Add(new iTextSharp.text.Paragraph("Physicist : " + Physicist));


            document.Add(new iTextSharp.text.Paragraph("Course : " + PlanCourse));
            document.Add(new iTextSharp.text.Paragraph("Plan : " + Plan));
            document.Add(new iTextSharp.text.Paragraph("Document name : " + PlanCourse.Substring(0, 2) + Plan + " (Water)"));
            document.Add(new iTextSharp.text.Paragraph("Structure Set : " + StructureSet));
            document.Add(new iTextSharp.text.Paragraph("Image Device : " + ImageDevice));
            document.Add(new iTextSharp.text.Paragraph("Treatment Machine : " + TreatmentMachine));
            document.Add(new iTextSharp.text.Paragraph("Prescribed Dose : " + PrescribedDose));
            document.Add(new iTextSharp.text.Paragraph("Dose Per Fraction : " + DosePerFraction));
            document.Add(new iTextSharp.text.Paragraph("Number Of Fractions : " + NumberOfFractions));
            document.Add(new iTextSharp.text.Paragraph("Target : " + PTV));
            document.Add(new iTextSharp.text.Paragraph("Energy : " + Energy));
            document.Add(new iTextSharp.text.Paragraph("Total MU : " + TotalMU));
            document.Add(new iTextSharp.text.Paragraph("Normalization Value : " + NormalizationValue));
            document.Add(new iTextSharp.text.Paragraph(NormalizationMode));
            document.Add(new iTextSharp.text.Paragraph(" "));
            document.Add(new iTextSharp.text.Paragraph(" "));

            // Page for water override

            document.NewPage();

            PlanSetup qaPlanWater = qaPlanQW;
            string PlanCourseQW = USZ_ARTEMIS.Tools.ExtractData.GetCourse(context, qaPlanWater);
            string PlanQW = USZ_ARTEMIS.Tools.ExtractData.GetPlan(context, qaPlanWater);
            string StructureSetQW = USZ_ARTEMIS.Tools.ExtractData.GetStructureSet(context, qaPlanWater);
            string ImageDeviceQW = USZ_ARTEMIS.Tools.ExtractData.GetImageDevice(context, qaPlanWater);
            string TreatmentMachineQW = USZ_ARTEMIS.Tools.ExtractData.GetTreatmentMachine(context, qaPlanWater);
            string PrescribedDoseQW = USZ_ARTEMIS.Tools.ExtractData.GetPrescribedDose(context, qaPlanWater).ToString();
            string DosePerFractionQW = USZ_ARTEMIS.Tools.ExtractData.GetDosePerFraction(context, qaPlanWater);
            string NumberOfFractionsQW = USZ_ARTEMIS.Tools.ExtractData.GetNumberOfFractions(context, qaPlanWater);
            string PTVQW = USZ_ARTEMIS.Tools.ExtractData.FindPTV(context, qaPlanWater).ToString();
            string EnergyQW = USZ_ARTEMIS.Tools.ExtractData.GetEnergy(context, qaPlanWater);
            string TotalMUQW = USZ_ARTEMIS.Tools.ExtractData.GetTotalMu(qaPlanWater).ToString("0.000");
            double QWMUDifference = Math.Abs(USZ_ARTEMIS.Tools.ExtractData.GetTotalMu(orgPlan) - USZ_ARTEMIS.Tools.ExtractData.GetTotalMu(qaPlanWater));
            string QWMUDifferencestring = (USZ_ARTEMIS.Tools.ExtractData.GetTotalMu(orgPlan) - USZ_ARTEMIS.Tools.ExtractData.GetTotalMu(qaPlanWater)).ToString("0.000");
            string NormalizationValueQW = USZ_ARTEMIS.Tools.ExtractData.GetNormalizationValue(context, qaPlanWater);
            string NormalizationModeQW = USZ_ARTEMIS.Tools.ExtractData.GetNormalizationMode(context, qaPlanWater);

            if (USZ_ARTEMIS.Tools.ExtractData.GetPTVDmean(context, qaPlanWater) < 0.1) { System.Windows.Forms.MessageBox.Show("WARNING: The QA plan had no dose calculated yet. Perform QA and calculate dose first!"); }


            document.Add(new iTextSharp.text.Paragraph("Date : " + today.ToString()));
            document.Add(new iTextSharp.text.Paragraph("Patient ID : " + PatientID));
            document.Add(new iTextSharp.text.Paragraph("Patient Name : " + PatientName));
            document.Add(new iTextSharp.text.Paragraph("Oncologist : " + Oncologist));
            document.Add(new iTextSharp.text.Paragraph("Physicist : " + Physicist));

            document.Add(new iTextSharp.text.Paragraph("Course Water Override : " + PlanCourseQW));
            document.Add(new iTextSharp.text.Paragraph("Plan Water Override : " + PlanQW));
            document.Add(new iTextSharp.text.Paragraph("Structure Set Water Override : " + StructureSetQW));
            document.Add(new iTextSharp.text.Paragraph("Image Device Water Override : " + ImageDeviceQW));
            document.Add(new iTextSharp.text.Paragraph("Treatment Machine Water Override : " + TreatmentMachineQW));
            document.Add(new iTextSharp.text.Paragraph("Prescribed Dose Water Override : " + PrescribedDoseQW));
            document.Add(new iTextSharp.text.Paragraph("Dose Per Fraction Water Override : " + DosePerFractionQW));
            document.Add(new iTextSharp.text.Paragraph("Number Of Fractions Water Override : " + NumberOfFractionsQW));
            document.Add(new iTextSharp.text.Paragraph("Target Water Override : " + PTVQW));
            document.Add(new iTextSharp.text.Paragraph("Energy Water Override : " + EnergyQW));
            document.Add(new iTextSharp.text.Paragraph("Total MU Water Override : " + TotalMUQW));

            // threshold values for red/green
            double MUDiffTreshold = 0.5;
            double UpperThresholdW = 4.0;
            double LowerThresholdW = -1.0;


            if (QWMUDifference > MUDiffTreshold || QWMUDifference.ToString().Contains("NaN"))
            {
                //BaseColor red = new BaseColor(Color.Red);
                Font red = new Font(Font.FontFamily.UNDEFINED, 12, Font.BOLD, BaseColor.RED); //RED id != 0
                Chunk RedMU = new Chunk(QWMUDifferencestring, red);
                Chunk text = new Chunk("Difference of MU with respect to original plan : ");


                Paragraph CheckRedMU = new Paragraph();
                CheckRedMU.Add(text);
                CheckRedMU.Add(RedMU);
                document.Add(CheckRedMU);
            }

            else if (QWMUDifference < MUDiffTreshold)
            {
                document.Add(new iTextSharp.text.Paragraph("Difference of MU with respect to original plan : " + QWMUDifferencestring));
            }

            document.Add(new iTextSharp.text.Paragraph("Normalization Value Water Override : " + NormalizationValueQW));
            document.Add(new iTextSharp.text.Paragraph(NormalizationModeQW));
            document.Add(new iTextSharp.text.Paragraph(" "));



            PdfPTable table = new PdfPTable(6);
            PdfPCell cell = new PdfPCell(new Phrase("Water Body Mask Override"));

            table.HorizontalAlignment = 0;
            table.TotalWidth = 540f;
            table.LockedWidth = true;
            float[] widths = new float[] { 100f, 100f, 140f, 70f, 70f, 60f };
            table.SetWidths(widths);

            cell.Colspan = 6;
            cell.HorizontalAlignment = 1; //0=Left, 1=Centre, 2=Right

            table.AddCell(cell);

            table.AddCell("Structure");
            table.AddCell("Priority");
            table.AddCell("Objective");
            table.AddCell("ActualValue");
            table.AddCell("ActualValue QW");
            table.AddCell("Difference [%]");


            table.AddCell(PTV);
            table.AddCell("");
            table.AddCell("Dmean [%]");
            double DMean = USZ_ARTEMIS.Tools.ExtractData.GetPTVDmean(context, orgPlan);
            table.AddCell(DMean.ToString("0.00"));
            double DMeanQW = USZ_ARTEMIS.Tools.ExtractData.GetPTVDmean(context, qaPlanWater);
            table.AddCell(DMeanQW.ToString("0.00"));
            double DiffDmeanQW = DMeanQW - DMean;
            if (DiffDmeanQW >= LowerThresholdW && DiffDmeanQW <= UpperThresholdW)
            {

                PdfPCell cellDiff = new PdfPCell(new Phrase(DiffDmeanQW.ToString("0.00")));
                cellDiff.BackgroundColor = (new BaseColor(0, 128, 0)); //Green
                table.AddCell(cellDiff);

            }
            else
            {
                PdfPCell cellDiff = new PdfPCell(new Phrase(DiffDmeanQW.ToString("0.00")));
                cellDiff.BackgroundColor = (new BaseColor(255, 0, 0)); //Red
                table.AddCell(cellDiff);

            }

            table.AddCell(PTV);
            table.AddCell("");
            table.AddCell("Dmax [%]");
            double DMax = USZ_ARTEMIS.Tools.ExtractData.GetPTVDmax(context, orgPlan);
            table.AddCell(DMax.ToString("0.00"));
            double DMaxQW = USZ_ARTEMIS.Tools.ExtractData.GetPTVDmax(context, qaPlanWater);
            table.AddCell(DMaxQW.ToString("0.00"));
            double DiffDmaxQW = DMaxQW - DMax;
            PdfPCell cellDiffMax = new PdfPCell(new Phrase(DiffDmaxQW.ToString("0.00")));
            table.AddCell(cellDiffMax);

            table.AddCell(PTV);
            table.AddCell("");
            table.AddCell("D0.03cc [%]");
            double D03cc = USZ_ARTEMIS.Tools.ExtractData.GetPTVD03(context, orgPlan);
            table.AddCell(D03cc.ToString("0.00"));
            double D03ccQW = USZ_ARTEMIS.Tools.ExtractData.GetPTVD03(context, qaPlanWater);
            table.AddCell(D03ccQW.ToString("0.00"));
            double DiffD03ccQW = D03ccQW - D03cc;
            PdfPCell cellDiff03 = new PdfPCell(new Phrase(DiffD03ccQW.ToString("0.00")));
            table.AddCell(cellDiff03);

            table.AddCell(PTV);
            table.AddCell("");
            table.AddCell("D2% [%]");
            double D2 = USZ_ARTEMIS.Tools.ExtractData.GetPTVD2(context, orgPlan);
            table.AddCell(D2.ToString("0.00"));
            double D2QW = USZ_ARTEMIS.Tools.ExtractData.GetPTVD2(context, qaPlanWater);
            table.AddCell(D2QW.ToString("0.00"));
            double DiffD2QW = D2QW - D2;
            PdfPCell cellDiff2 = new PdfPCell(new Phrase(DiffD2QW.ToString("0.00")));
            table.AddCell(cellDiff2);

            table.AddCell(PTV);
            table.AddCell("");
            table.AddCell("D95% [%]");
            double D95 = USZ_ARTEMIS.Tools.ExtractData.GetPTVD95(context, orgPlan);
            table.AddCell(D95.ToString("0.00"));
            double D95QW = USZ_ARTEMIS.Tools.ExtractData.GetPTVD95(context, qaPlanWater);
            table.AddCell(D95QW.ToString("0.00"));
            double DiffD95QW = D95QW - D95;
            PdfPCell cellDiff95 = new PdfPCell(new Phrase(DiffD95QW.ToString("0.00")));
            table.AddCell(cellDiff95);

            table.AddCell(PTV);
            table.AddCell("");
            table.AddCell("D98% [%]");
            double D98 = USZ_ARTEMIS.Tools.ExtractData.GetPTVD98(context, orgPlan);
            table.AddCell(D98.ToString("0.00"));
            double D98QW = USZ_ARTEMIS.Tools.ExtractData.GetPTVD98(context, qaPlanWater);
            table.AddCell(D98QW.ToString("0.00"));
            double DiffD98QW = D98QW - D98;
            PdfPCell cellDiff98 = new PdfPCell(new Phrase(DiffD98QW.ToString("0.00")));
            table.AddCell(cellDiff98);

            if (orgPlan.GetClinicalGoals() != null && qaPlanWater.GetClinicalGoals() != null)
            {
                List<EvalClinicalGoals> EvalCG_QW = USZ_ARTEMIS.Tools.EvalClinicalGoals.GetClinicalGoals(context, orgPlan, qaPlanWater);


                for (int i = 0; i < EvalCG_QW.Count; i++)
                {
                    string structureId = EvalCG_QW[i].structureId;
                    table.AddCell(structureId);
                    string priority = EvalCG_QW[i].priority;
                    table.AddCell(priority);
                    string objective = EvalCG_QW[i].objective;
                    table.AddCell(objective);
                    string actualValue = EvalCG_QW[i].actualValue.ToString("0.00");
                    table.AddCell(actualValue);
                    string actualValueqa = EvalCG_QW[i].actualValueqa.ToString("0.00");
                    table.AddCell(actualValueqa);

                    double percDiff = EvalCG_QW[i].percDiff;

                    string percDiffstring = percDiff.ToString("0.00");
                    PdfPCell cellDiff = new PdfPCell(new Phrase(percDiffstring));

                    table.AddCell(cellDiff);

                }
            }

            document.Add(table);


            // Close the document  
            document.Close();
            // Close the writer instance  
            writer.Close();
            // Always close open filehandles explicity  
            fs.Close();

            // Open the file for the user
            System.Diagnostics.Process.Start(PathNamePdf);

        }

    }
}
