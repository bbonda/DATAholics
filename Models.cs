using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
namespace AccurateHireIQ;
public class Observable : INotifyPropertyChanged
{
 public event PropertyChangedEventHandler? PropertyChanged;
 protected void Changed([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
 protected bool Set<T>(ref T field,T value,[CallerMemberName]string? name=null){if(EqualityComparer<T>.Default.Equals(field,value))return false;field=value;Changed(name);return true;}
}
public class Command(Action<object?> action, Func<bool>? can=null) : ICommand
{
 public event EventHandler? CanExecuteChanged {add=>CommandManager.RequerySuggested+=value;remove=>CommandManager.RequerySuggested-=value;}
 public bool CanExecute(object? p)=>can?.Invoke()??true;
 public void Execute(object? p)=>action(p);
}
public record Candidate(long RowId,string CandidateId,string Name,string Role,string Location,string Status,string Remarks,int SourceRow,string Sheet,string File,string OriginalJson)
{
 public string ClassificationBasis{get;init;}="";
 public string ClassificationReason{get;init;}="";
 public string StatusLabel=>Status=="STOP"?"REJECT (STOP)":Status;
 public override string ToString()=>$"Source row {SourceRow} · {Status}";
}
public record Finding(long CandidateRow,string CandidateId,int SourceRow,string Type,string Column,string Value,string Explanation,string FollowUp);
public record CountItem(string Name,long Count);
public record Batch(long Id,string File,string Imported,long Count,string Sheet);
public record Mapping(string Id,string Name,string Status,string Role,string Location,string Remarks);
public record Filter(string Status="",string Search="",string Role="",string Location="",string Finding="",string Sort="SourceRow",bool Desc=false,string SourceColumn="",string SourceText="");
public record Preview(string Sheet,List<string> Headers,List<Dictionary<string,string>> Rows);
public record ToolLog(string Time,string Tool,string Result);
public class Settings
{
 public string Endpoint {get;set;}="http://localhost:11434/v1";
 public string Model {get;set;}="";
 public string ProtectedKey {get;set;}="";
 public bool AllowExternal {get;set;}
 public bool AllowExports {get;set;}=true;
 public bool MaskPersonal {get;set;}=true;
 public int TimeoutSeconds {get;set;}=90;
}
