using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using ExcelDataReader;
using Microsoft.Data.Sqlite;
namespace AccurateHireIQ;
public class ImportService(Store store)
{
 public static void SaveClassification(SqliteConnection c,SqliteTransaction tx,long row,Classification result)
 {
  using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="INSERT OR REPLACE INTO Classifications VALUES($r,$v,$b,$reason,$e)";q.Parameters.AddWithValue("$r",row);q.Parameters.AddWithValue("$v",ScreeningClassifier.Version);q.Parameters.AddWithValue("$b",result.Basis);q.Parameters.AddWithValue("$reason",result.Reason);q.Parameters.AddWithValue("$e",JsonSerializer.Serialize(result.Evidence));q.ExecuteNonQuery();
 }
 public int UpgradeExisting()
 {
  using var c=store.Open();using var tx=c.BeginTransaction();int updated=0;long last=0;
  while(true){var rows=new List<(long id,string status,string json)>();using(var read=c.CreateCommand()){read.Transaction=tx;read.CommandText="SELECT RowId,Status,OriginalJson FROM Candidates WHERE RowId>$last AND NOT EXISTS(SELECT 1 FROM Classifications k WHERE k.CandidateRow=Candidates.RowId) ORDER BY RowId LIMIT 500";read.Parameters.AddWithValue("$last",last);using var r=read.ExecuteReader();while(r.Read())rows.Add((r.GetInt64(0),r.GetString(1),r.GetString(2)));}if(rows.Count==0)break;
   foreach(var row in rows){last=row.id;var fields=JsonSerializer.Deserialize<Dictionary<string,string>>(row.json)!;var map=Guess(fields.Keys.ToList());
    if(map.Status==""&&row.status=="UNCLASSIFIED"){using var previousMapping=c.CreateCommand();previousMapping.Transaction=tx;previousMapping.CommandText="SELECT SourceColumn FROM DataQualityFindings WHERE CandidateRow=$r AND Type IN ('Missing status','Invalid status') LIMIT 1";previousMapping.Parameters.AddWithValue("$r",row.id);var column=previousMapping.ExecuteScalar()?.ToString();if(column!=null&&fields.ContainsKey(column))map=map with{Status=column};}
    var result=map.Status==""&&row.status=="UNCLASSIFIED"?ScreeningClassifier.Evaluate(fields,map):new Classification(row.status,"Source overall result","Preserves the previously imported overall result.",map.Status!=""?[new(map.Status,fields.GetValueOrDefault(map.Status,""))]:[]);SaveClassification(c,tx,row.id,result);
    if(result.Status!=row.status){using var update=c.CreateCommand();update.Transaction=tx;update.CommandText="UPDATE Candidates SET Status=$s WHERE RowId=$r; DELETE FROM DataQualityFindings WHERE CandidateRow=$r AND Type='Missing status';";update.Parameters.AddWithValue("$s",result.Status);update.Parameters.AddWithValue("$r",row.id);update.ExecuteNonQuery();updated++;}
   }
  }tx.Commit();if(updated>0)store.Audit("Classification upgrade",$"Applied {ScreeningClassifier.Version} to {updated} previously unclassified records. Original values preserved.");return updated;
 }
 public static string Classify(string? value)=>value?.Trim().ToUpperInvariant() is "PASS" or "REVIEW" or "STOP" ? value.Trim().ToUpperInvariant():"UNCLASSIFIED";
 public static string SafeCell(string value)=>value.TrimStart().StartsWithAny('=','+','-','@','\t','\r')?"'"+value:value;
 public IEnumerable<(string sheet,int row,List<string> values)> Read(string path,string? selectedSheet=null)
 {
  if(Path.GetExtension(path).Equals(".csv",StringComparison.OrdinalIgnoreCase))
  {using var sr=new StreamReader(path);using var csv=new CsvReader(sr,new CsvConfiguration(CultureInfo.InvariantCulture){HasHeaderRecord=false,MissingFieldFound=null});int row=0;while(csv.Read())yield return("CSV",++row,csv.Parser.Record!.Select(x=>x??"").ToList());}
  else
  {Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);using var fs=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);using var r=ExcelReaderFactory.CreateReader(fs);do{if(selectedSheet!=null&&r.Name!=selectedSheet)continue;int row=0;while(r.Read()){var a=new List<string>();for(int i=0;i<r.FieldCount;i++)a.Add(r.GetValue(i) is DateTime dt?dt.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture):Convert.ToString(r.GetValue(i),CultureInfo.InvariantCulture)??"");yield return(r.Name,++row,a);}if(selectedSheet==null)break;}while(r.NextResult());}
 }
 public List<string> Sheets(string path){if(Path.GetExtension(path).Equals(".csv",StringComparison.OrdinalIgnoreCase))return["CSV"];Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);using var fs=File.OpenRead(path);using var r=ExcelReaderFactory.CreateReader(fs);var a=new List<string>();do{a.Add(r.Name);}while(r.NextResult());return a;}
 public Preview Preview(string path,string? sheet=null){var rows=Read(path,sheet).Take(7).ToList();if(rows.Count==0)throw new InvalidDataException("The selected sheet is empty.");var headers=Headers(rows[0].values);return new(rows[0].sheet,headers,rows.Skip(1).Select(r=>headers.Select((h,i)=>(h,v:i<r.values.Count?r.values[i]:"")).ToDictionary(x=>x.h,x=>x.v)).ToList());}
 static List<string> Headers(List<string> source){var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var a=new List<string>();for(int i=0;i<source.Count;i++){var h=string.IsNullOrWhiteSpace(source[i])?$"Column {i+1}":source[i];var unique=h;int n=2;while(!used.Add(unique))unique=$"{h} [{n++}]";a.Add(unique);}return a;}
 public static Mapping Guess(List<string> h){string Find(params string[] names)=>h.FirstOrDefault(x=>names.Any(n=>x.Equals(n,StringComparison.OrdinalIgnoreCase)))??"";return new(Find("Candidate ID","CandidateId","ID"),Find("Candidate Name","Full Name","Name"),Find("Overall Screening Result","Screening Status","Overall Status","Screening Result","Final"),Find("Job Role","Role"),Find("Location","State","City"),Find("Screening Remarks","Verification Remarks","Remarks"));}
 public long Import(string path,Mapping map,string sheet,IProgress<string>? progress,CancellationToken ct)
 {
  using var c=store.Open();using var tx=c.BeginTransaction();var iterator=Read(path,sheet).GetEnumerator();if(!iterator.MoveNext())throw new InvalidDataException("Empty file");var h=Headers(iterator.Current.values);
  if(string.IsNullOrEmpty(map.Name)||!h.Contains(map.Name))throw new InvalidDataException("Map a candidate-name column before importing.");
  foreach(var m in new[]{map.Id,map.Status,map.Role,map.Location,map.Remarks})if(m!=""&&!h.Contains(m))throw new InvalidDataException("Mapped column not found: "+m);
  using var batch=c.CreateCommand();batch.Transaction=tx;batch.CommandText="INSERT INTO UploadBatches(File,Imported,Count,Sheet,Headers) VALUES($f,$t,0,$s,$h);SELECT last_insert_rowid();";batch.Parameters.AddWithValue("$f",Path.GetFileName(path));batch.Parameters.AddWithValue("$t",DateTimeOffset.Now.ToString("O"));batch.Parameters.AddWithValue("$s",sheet);batch.Parameters.AddWithValue("$h",JsonSerializer.Serialize(h));long id=(long)batch.ExecuteScalar()!;int count=0;
  using var ins=c.CreateCommand();ins.Transaction=tx;ins.CommandText="INSERT INTO Candidates(BatchId,CandidateId,Name,Role,Location,Status,Remarks,SourceRow,Sheet,File,OriginalJson) VALUES($b,$i,$n,$r,$l,$s,$m,$row,$sh,$f,$j);SELECT last_insert_rowid();";foreach(var p in new[]{"$b","$i","$n","$r","$l","$s","$m","$row","$sh","$f","$j"})ins.Parameters.Add(new SqliteParameter(p,""));ins.Prepare();
  void Issue(long row,string type,string col,string val,string reason,string follow="Verify the source record with the screening team."){using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="INSERT INTO DataQualityFindings(CandidateRow,Type,SourceColumn,SourceValue,Explanation,FollowUp) VALUES($r,$t,$c,$v,$e,$f)";q.Parameters.AddWithValue("$r",row);q.Parameters.AddWithValue("$t",type);q.Parameters.AddWithValue("$c",col);q.Parameters.AddWithValue("$v",val);q.Parameters.AddWithValue("$e",reason);q.Parameters.AddWithValue("$f",follow);q.ExecuteNonQuery();}
  try{while(iterator.MoveNext())
  {
   ct.ThrowIfCancellationRequested();var source=iterator.Current;var d=h.Select((v,i)=>(v,text:i<source.values.Count?source.values[i]:"")).ToDictionary(x=>x.v,x=>x.text);if(d.Values.All(string.IsNullOrEmpty))continue;
   string V(string key)=>key!=""&&d.TryGetValue(key,out var v)?v:"";var cid=V(map.Id);var classification=ScreeningClassifier.Evaluate(d,map);var status=classification.Status;var json=JsonSerializer.Serialize(d);
   object[] vals=[id,cid,V(map.Name),V(map.Role),V(map.Location),status,V(map.Remarks),source.row,source.sheet,Path.GetFileName(path),json];for(int j=0;j<vals.Length;j++)ins.Parameters[j].Value=vals[j];long row=(long)ins.ExecuteScalar()!;
   using(var original=c.CreateCommand()){original.Transaction=tx;original.CommandText="INSERT INTO OriginalCandidateFields VALUES($r,$j)";original.Parameters.AddWithValue("$r",row);original.Parameters.AddWithValue("$j",json);original.ExecuteNonQuery();}
   SaveClassification(c,tx,row,classification);
   if(string.IsNullOrWhiteSpace(cid))Issue(row,"Missing ID",map.Id==""?"[Unmapped Candidate ID]":map.Id,"","No candidate ID supplied. Use batch, sheet and source row for traceability.");
   if(string.IsNullOrWhiteSpace(V(map.Name)))Issue(row,"Missing name",map.Name,"","Candidate name is empty.");
   if(status=="UNCLASSIFIED")Issue(row,string.IsNullOrWhiteSpace(V(map.Status))?"Missing status":"Invalid status",map.Status==""?"[Screening classification]":map.Status,V(map.Status),classification.Reason);
   foreach(var pair in d)
   {
    if(new[]{map.Id,map.Name,map.Status,map.Role,map.Location,map.Remarks}.Contains(pair.Key)||!IsCheck(pair.Key))continue;
    if(string.IsNullOrWhiteSpace(pair.Value))Issue(row,"Missing verification",pair.Key,pair.Value,"Verification field is empty; applicability must be confirmed.","Confirm whether this check applies and request missing information.");
    else if(IsPending(pair.Value))Issue(row,"Pending verification",pair.Key,pair.Value,"The source value explicitly indicates pending or incomplete verification.","Follow up with the verification owner; preserve the existing overall result.");
    else if(!new[]{"pass","clear","verified","yes","n/a","not applicable","complete","completed"}.Contains(pair.Value.Trim().ToLowerInvariant()))Issue(row,"Source finding",pair.Key,pair.Value,"Nonstandard check value or source finding; this is not an inferred screening decision.","Read the source finding and resolve any documentation questions.");
    var explicitStop=System.Text.RegularExpressions.Regex.IsMatch(pair.Value.Trim(),@"^(?:hard\s+stop|stop|reject(?:ed)?)(?:\s*[:;\-/–—]|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if((status=="PASS"&&(IsPending(pair.Value)||explicitStop))||(status=="REVIEW"&&explicitStop))Issue(row,"Conflicting fields",pair.Key,pair.Value,$"Overall status is {status} while this field reports {(explicitStop?"an explicit STOP finding":"pending verification")}.","Ask an authorized reviewer to reconcile the source fields; do not overwrite the supplied overall result.");
   }
   count++;if(count%250==0)progress?.Report($"Imported {count:N0} records…");
  }
  ct.ThrowIfCancellationRequested();using var du=c.CreateCommand();du.Transaction=tx;du.CommandText="""
  INSERT INTO DataQualityFindings(CandidateRow,Type,SourceColumn,SourceValue,Explanation,FollowUp)
  SELECT RowId,'Duplicate ID',$col,CandidateId,'Candidate ID occurs more than once in this batch. All rows were retained.','Reconcile duplicate identifiers with the source owner.' FROM Candidates
  WHERE BatchId=$b AND trim(CandidateId)<>'' AND trim(lower(CandidateId)) IN(SELECT trim(lower(CandidateId)) FROM Candidates WHERE BatchId=$b AND trim(CandidateId)<>'' GROUP BY trim(lower(CandidateId)) HAVING COUNT(*)>1);
  UPDATE UploadBatches SET Count=$n WHERE Id=$b;
  """;du.Parameters.AddWithValue("$col",map.Id);du.Parameters.AddWithValue("$b",id);du.Parameters.AddWithValue("$n",count);du.ExecuteNonQuery();tx.Commit();store.ActiveBatch=id;store.Audit("Import",$"Imported {count} records; original fields preserved.");return count;
  }finally{iterator.Dispose();}
 }
 public static bool IsCheck(string name)=>new[]{"verification","check","license","criminal","court","drug","alcohol","record","violation","discrepancy","conviction","match","authorization","clear","verified","pending","offense","misdemeanor","felony","accident","fraud","suspension","speeding","endorsement","employer","gap","falsified","medical","insurance","csa","hit-and-run","dwls","screening"}.Any(x=>name.Contains(x,StringComparison.OrdinalIgnoreCase));
 public static bool IsPending(string value)=>new[]{"pending","incomplete","unverifiable","missing","delay","awaiting"}.Any(x=>value.Contains(x,StringComparison.OrdinalIgnoreCase));
 public long Export(string path,Filter filter,IProgress<string>? progress,CancellationToken ct,bool analysis=false)
 {
  if(!store.ReadSettings().AllowExports)throw new InvalidOperationException("Exports are disabled in Settings.");var headers=store.Headers();string AddHeader(string name){var key=name;int i=2;while(headers.Contains(key))key=$"{name} ({i++})";headers.Add(key);return key;}var statusHeader=AddHeader("AccurateHire Classification");var basisHeader=AddHeader("AccurateHire Classification Basis");var reasonHeader=AddHeader("AccurateHire Classification Reason");
  Dictionary<string,string> ExportFields(Candidate r){var d=JsonSerializer.Deserialize<Dictionary<string,string>>(r.OriginalJson)!;d[statusHeader]=r.StatusLabel;d[basisHeader]=r.ClassificationBasis;d[reasonHeader]=r.ClassificationReason;return d;}
  long total=store.Count(filter);int offset=0;var temp=path+".partial"+Path.GetExtension(path);
  try{
   if(Path.GetExtension(path).Equals(".csv",StringComparison.OrdinalIgnoreCase))
   {using var sw=new StreamWriter(temp,false,new UTF8Encoding(true));using var csv=new CsvWriter(sw,CultureInfo.InvariantCulture);foreach(var h in headers)csv.WriteField(SafeCell(h));csv.NextRecord();while(offset<total){foreach(var r in store.Query(filter,offset,1000)){ct.ThrowIfCancellationRequested();var d=ExportFields(r);foreach(var h in headers)csv.WriteField(SafeCell(d.GetValueOrDefault(h,"")));csv.NextRecord();offset++;}progress?.Report($"Exported {offset:N0} / {total:N0}");}}
   else
   {using var wb=new XLWorkbook();var ws=wb.AddWorksheet("Candidates");for(int j=0;j<headers.Count;j++)ws.Cell(1,j+1).Value=SafeCell(headers[j]);while(offset<total){foreach(var r in store.Query(filter,offset,1000)){ct.ThrowIfCancellationRequested();var d=ExportFields(r);for(int j=0;j<headers.Count;j++)ws.Cell(offset+2,j+1).Value=SafeCell(d.GetValueOrDefault(headers[j],""));offset++;}progress?.Report($"Exported {offset:N0} / {total:N0}");}ws.Row(1).Style.Font.Bold=true;ws.Row(1).Style.Fill.BackgroundColor=XLColor.FromHtml("#142D4E");ws.Row(1).Style.Font.FontColor=XLColor.White;ws.SheetView.FreezeRows(1);ws.Columns().Width=24;if(headers.Count>0)ws.Range(1,1,Math.Max(1,offset+1),headers.Count).SetAutoFilter();
    if(analysis){var a=wb.AddWorksheet("Operational findings");string[] names=["Source row","Candidate ID","Finding type","Source column","Source value","Explanation","Administrative follow-up"];for(int j=0;j<names.Length;j++)a.Cell(1,j+1).Value=names[j];int n=2;for(int start=0;;start+=1000){var findings=store.Findings(limit:1000,offset:start);if(findings.Count==0)break;foreach(var f in findings){ct.ThrowIfCancellationRequested();string[] vals=[f.SourceRow.ToString(),f.CandidateId,f.Type,f.Column,f.Value,f.Explanation,f.FollowUp];for(int j=0;j<vals.Length;j++)a.Cell(n,j+1).Value=SafeCell(vals[j]);n++;}}a.Row(1).Style.Font.Bold=true;a.Columns().Width=30;a.SheetView.FreezeRows(1);}
    ct.ThrowIfCancellationRequested();wb.SaveAs(temp);}
   ct.ThrowIfCancellationRequested();File.Move(temp,path,true);store.RecordReport(path,total);return total;
  }finally{if(File.Exists(temp))File.Delete(temp);}
 }
}
static class StringExtensions{public static bool StartsWithAny(this string s,params char[] chars)=>s.Length>0&&chars.Contains(s[0]);}
