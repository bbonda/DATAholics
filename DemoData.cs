using ClosedXML.Excel;
namespace AccurateHireIQ;
public static class DemoData
{
 public static void Generate(string path)
 {
  using var wb=new XLWorkbook();var ws=wb.AddWorksheet("Synthetic candidates");string[] headers=["Candidate ID","Candidate Name","Job Role","Location","Overall Screening Result","Driving License Verification","Employment Verification","Screening Remarks"];
  for(int c=0;c<headers.Length;c++)ws.Cell(1,c+1).Value=headers[c];
  for(int i=0;i<10000;i++){var status=i<7500?"PASS":i<9500?"REVIEW":"STOP";string[] fields=[$"DEMO{i+1:00000}",$"Fictional Candidate {i+1:00000}",new[]{"Delivery Associate","Truck Driver","Warehouse Worker"}[i%3],new[]{"Atlanta","Chicago","Dallas","Phoenix","Seattle"}[i%5],status,status=="REVIEW"&&i%2==0?"Pending":"Verified",i%113==0?"":"Verified",status=="REVIEW"?"Source result is REVIEW; verification follow-up is recorded in the relevant check fields.":status=="STOP"?"Source overall result is STOP. No underlying rationale supplied in this fictional dataset.":"Source overall result is PASS."];
   for(int c=0;c<fields.Length;c++)ws.Cell(i+2,c+1).Value=fields[c];}
  ws.Row(1).Style.Font.Bold=true;ws.Row(1).Style.Fill.BackgroundColor=XLColor.FromHtml("#142D4E");ws.Row(1).Style.Font.FontColor=XLColor.White;ws.Columns().Width=26;ws.Column(8).Width=75;ws.SheetView.FreezeRows(1);ws.Range(1,1,10001,8).SetAutoFilter();wb.SaveAs(path);
 }
}
