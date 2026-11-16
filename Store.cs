using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.IO;
namespace AccurateHireIQ;
public class Store(string root)
{
 public string Root=>root;
 public long ActiveBatch {get;set;}
 public SqliteConnection Open(){var c=new SqliteConnection($"Data Source={Path.Combine(root,"accuratehire.db")};Default Timeout=60");c.Open();using var p=c.CreateCommand();p.CommandText="PRAGMA foreign_keys=ON;";p.ExecuteNonQuery();return c;}
 public void Initialize()
 {
  using var c=Open();using var q=c.CreateCommand();q.CommandText="""
  PRAGMA journal_mode=WAL;
  CREATE TABLE IF NOT EXISTS UploadBatches(Id INTEGER PRIMARY KEY,File TEXT,Imported TEXT,Count INTEGER,Sheet TEXT,Headers TEXT);
  CREATE TABLE IF NOT EXISTS Candidates(RowId INTEGER PRIMARY KEY,BatchId INTEGER REFERENCES UploadBatches(Id) ON DELETE CASCADE,CandidateId TEXT,Name TEXT,Role TEXT,Location TEXT,Status TEXT,Remarks TEXT,SourceRow INTEGER,Sheet TEXT,File TEXT,OriginalJson TEXT);
  CREATE INDEX IF NOT EXISTS ix_status ON Candidates(BatchId,Status);
  CREATE INDEX IF NOT EXISTS ix_id ON Candidates(BatchId,CandidateId);
  CREATE INDEX IF NOT EXISTS ix_role ON Candidates(BatchId,Role,Location);
  CREATE TABLE IF NOT EXISTS OriginalCandidateFields(CandidateRow INTEGER PRIMARY KEY REFERENCES Candidates(RowId) ON DELETE CASCADE,FieldsJson TEXT);
  CREATE TABLE IF NOT EXISTS DataQualityFindings(Id INTEGER PRIMARY KEY,CandidateRow INTEGER REFERENCES Candidates(RowId) ON DELETE CASCADE,Type TEXT,SourceColumn TEXT,SourceValue TEXT,Explanation TEXT,FollowUp TEXT);
  CREATE INDEX IF NOT EXISTS ix_findings ON DataQualityFindings(CandidateRow,Type);
  CREATE TABLE IF NOT EXISTS AgentExecutionHistory(Id INTEGER PRIMARY KEY,BatchId INTEGER,Time TEXT,Tool TEXT,Result TEXT);
  CREATE TABLE IF NOT EXISTS GeneratedReports(Id INTEGER PRIMARY KEY,BatchId INTEGER,Time TEXT,Path TEXT,Count INTEGER);
  CREATE TABLE IF NOT EXISTS ApplicationSettings(Key TEXT PRIMARY KEY,Value TEXT);
  CREATE TABLE IF NOT EXISTS Classifications(CandidateRow INTEGER PRIMARY KEY REFERENCES Candidates(RowId) ON DELETE CASCADE,Version TEXT,Basis TEXT,Reason TEXT,EvidenceJson TEXT);
  """;q.ExecuteNonQuery();ActiveBatch=History().FirstOrDefault()?.Id??0;
 }
 public List<Batch> History(){using var c=Open();using var q=c.CreateCommand();q.CommandText="SELECT Id,File,Imported,Count,Sheet FROM UploadBatches ORDER BY Id DESC";using var r=q.ExecuteReader();var a=new List<Batch>();while(r.Read())a.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetInt64(3),r.GetString(4)));return a;}
 public Settings ReadSettings(){using var c=Open();using var q=c.CreateCommand();q.CommandText="SELECT Value FROM ApplicationSettings WHERE Key='settings'";return JsonSerializer.Deserialize<Settings>(q.ExecuteScalar()?.ToString()??"{}")??new();}
 public void SaveSettings(Settings settings){using var c=Open();using var q=c.CreateCommand();q.CommandText="INSERT OR REPLACE INTO ApplicationSettings VALUES('settings',$v)";q.Parameters.AddWithValue("$v",JsonSerializer.Serialize(settings));q.ExecuteNonQuery();}
 private string Where(SqliteCommand q,Filter f)
 {
  q.Parameters.AddWithValue("$b",ActiveBatch);var w="c.BatchId=$b";
  if(!string.IsNullOrEmpty(f.Status)){w+=" AND c.Status=$s";q.Parameters.AddWithValue("$s",f.Status);}
  if(!string.IsNullOrWhiteSpace(f.Search)){w+=" AND (instr(lower(c.CandidateId),lower($q))>0 OR instr(lower(c.Name),lower($q))>0 OR instr(lower(c.Remarks),lower($q))>0)";q.Parameters.AddWithValue("$q",f.Search);}
  if(!string.IsNullOrWhiteSpace(f.Role)){w+=" AND c.Role=$role";q.Parameters.AddWithValue("$role",f.Role);}
  if(!string.IsNullOrWhiteSpace(f.Location)){w+=" AND c.Location=$loc";q.Parameters.AddWithValue("$loc",f.Location);}
  if(!string.IsNullOrWhiteSpace(f.Finding)){w+=" AND EXISTS(SELECT 1 FROM DataQualityFindings d WHERE d.CandidateRow=c.RowId AND (d.Type=$f OR $f='ANY'))";q.Parameters.AddWithValue("$f",f.Finding);}
  if(!string.IsNullOrWhiteSpace(f.SourceColumn)||!string.IsNullOrWhiteSpace(f.SourceText)){w+=" AND EXISTS(SELECT 1 FROM json_each(c.OriginalJson) j WHERE ($sc='' OR j.key=$sc) AND instr(lower(CAST(j.value AS TEXT)),lower($sv))>0)";q.Parameters.AddWithValue("$sc",f.SourceColumn);q.Parameters.AddWithValue("$sv",f.SourceText);}
  return w;
 }
 public long Count(Filter f){using var c=Open();using var q=c.CreateCommand();q.CommandText="SELECT COUNT(*) FROM Candidates c WHERE "+Where(q,f);return (long)q.ExecuteScalar()!;}
 public List<Candidate> Query(Filter f,int offset=0,int limit=50)
 {
  using var c=Open();using var q=c.CreateCommand();var w=Where(q,f);var sort=new[]{"SourceRow","CandidateId","Name","Role","Location","Status"}.Contains(f.Sort)?f.Sort:"SourceRow";
  q.CommandText=$"SELECT RowId,CandidateId,Name,Role,Location,Status,Remarks,SourceRow,Sheet,File,OriginalJson,COALESCE(k.Basis,''),COALESCE(k.Reason,'') FROM Candidates c LEFT JOIN Classifications k ON k.CandidateRow=c.RowId WHERE {w} ORDER BY {sort} {(f.Desc?"DESC":"ASC")},RowId LIMIT $n OFFSET $o";q.Parameters.AddWithValue("$n",Math.Clamp(limit,1,5000));q.Parameters.AddWithValue("$o",Math.Max(0,offset));using var r=q.ExecuteReader();var a=new List<Candidate>();while(r.Read())a.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetInt32(7),r.GetString(8),r.GetString(9),r.GetString(10)){ClassificationBasis=r.GetString(11),ClassificationReason=r.GetString(12)});return a;
 }
 public List<CountItem> Counts(string column="Status")
 {
  if(!new[]{"Status","Role","Location"}.Contains(column))throw new ArgumentException("Invalid aggregation");using var c=Open();using var q=c.CreateCommand();q.CommandText=$"SELECT {column},COUNT(*) FROM Candidates WHERE BatchId=$b GROUP BY {column} ORDER BY COUNT(*) DESC";q.Parameters.AddWithValue("$b",ActiveBatch);using var r=q.ExecuteReader();var a=new List<CountItem>();while(r.Read())a.Add(new(r.GetString(0),r.GetInt64(1)));return a;
 }
 public List<Finding> Findings(string type="",long row=0,int limit=500,int offset=0)
 {
  using var c=Open();using var q=c.CreateCommand();q.CommandText="SELECT c.RowId,c.CandidateId,c.SourceRow,d.Type,d.SourceColumn,d.SourceValue,d.Explanation,d.FollowUp FROM DataQualityFindings d JOIN Candidates c ON c.RowId=d.CandidateRow WHERE c.BatchId=$b AND ($t='' OR d.Type=$t) AND ($r=0 OR c.RowId=$r) ORDER BY c.SourceRow,d.Id LIMIT $n OFFSET $o";q.Parameters.AddWithValue("$b",ActiveBatch);q.Parameters.AddWithValue("$t",type);q.Parameters.AddWithValue("$r",row);q.Parameters.AddWithValue("$n",limit);q.Parameters.AddWithValue("$o",offset);using var r=q.ExecuteReader();var a=new List<Finding>();while(r.Read())a.Add(new(r.GetInt64(0),r.GetString(1),r.GetInt32(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7)));return a;
 }
 public List<CountItem> FindingCounts(){using var c=Open();using var q=c.CreateCommand();q.CommandText="SELECT d.Type,COUNT(*) FROM DataQualityFindings d JOIN Candidates c ON c.RowId=d.CandidateRow WHERE c.BatchId=$b GROUP BY d.Type ORDER BY COUNT(*) DESC";q.Parameters.AddWithValue("$b",ActiveBatch);using var r=q.ExecuteReader();var a=new List<CountItem>();while(r.Read())a.Add(new(r.GetString(0),r.GetInt64(1)));return a;}
 public List<string> Headers(){using var c=Open();using var q=c.CreateCommand();q.CommandText="SELECT Headers FROM UploadBatches WHERE Id=$b";q.Parameters.AddWithValue("$b",ActiveBatch);return JsonSerializer.Deserialize<List<string>>(q.ExecuteScalar()?.ToString()??"[]")!;}
 public Classification? ClassificationFor(long row){using var c=Open();using var q=c.CreateCommand();q.CommandText="SELECT c.Status,k.Basis,k.Reason,k.EvidenceJson FROM Classifications k JOIN Candidates c ON c.RowId=k.CandidateRow WHERE k.CandidateRow=$r AND c.BatchId=$b";q.Parameters.AddWithValue("$r",row);q.Parameters.AddWithValue("$b",ActiveBatch);using var r=q.ExecuteReader();return r.Read()?new(r.GetString(0),r.GetString(1),r.GetString(2),JsonSerializer.Deserialize<List<ClassificationEvidence>>(r.GetString(3))!):null;}
 public Candidate? BySourceRow(int sourceRow){using var c=Open();using var q=c.CreateCommand();q.CommandText="SELECT RowId,CandidateId,Name,Role,Location,Status,Remarks,SourceRow,Sheet,File,OriginalJson FROM Candidates WHERE BatchId=$b AND SourceRow=$r";q.Parameters.AddWithValue("$b",ActiveBatch);q.Parameters.AddWithValue("$r",sourceRow);using var r=q.ExecuteReader();return r.Read()?new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetInt32(7),r.GetString(8),r.GetString(9),r.GetString(10)):null;}
 public void Audit(string tool,string result){using var c=Open();using var q=c.CreateCommand();q.CommandText="INSERT INTO AgentExecutionHistory(BatchId,Time,Tool,Result) VALUES($b,$t,$n,$r)";q.Parameters.AddWithValue("$b",ActiveBatch);q.Parameters.AddWithValue("$t",DateTimeOffset.Now.ToString("O"));q.Parameters.AddWithValue("$n",tool);q.Parameters.AddWithValue("$r",result);q.ExecuteNonQuery();}
 public List<ToolLog> Logs(){using var c=Open();using var q=c.CreateCommand();q.CommandText="SELECT Time,Tool,Result FROM AgentExecutionHistory WHERE BatchId=$b ORDER BY Id DESC LIMIT 100";q.Parameters.AddWithValue("$b",ActiveBatch);using var r=q.ExecuteReader();var a=new List<ToolLog>();while(r.Read())a.Add(new(r.GetString(0),r.GetString(1),r.GetString(2)));return a;}
 public void RecordReport(string path,long count){using var c=Open();using var q=c.CreateCommand();q.CommandText="INSERT INTO GeneratedReports(BatchId,Time,Path,Count) VALUES($b,$t,$p,$n)";q.Parameters.AddWithValue("$b",ActiveBatch);q.Parameters.AddWithValue("$t",DateTimeOffset.Now.ToString("O"));q.Parameters.AddWithValue("$p",path);q.Parameters.AddWithValue("$n",count);q.ExecuteNonQuery();Audit("Export",$"Exported {count:N0} records");}
 public void DeleteActive(){using var c=Open();using var q=c.CreateCommand();q.CommandText="DELETE FROM UploadBatches WHERE Id=$b; DELETE FROM AgentExecutionHistory WHERE BatchId=$b; DELETE FROM GeneratedReports WHERE BatchId=$b;";q.Parameters.AddWithValue("$b",ActiveBatch);q.ExecuteNonQuery();ActiveBatch=History().FirstOrDefault()?.Id??0;}
}
