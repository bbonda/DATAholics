using System.Text.RegularExpressions;
namespace AccurateHireIQ;

public record ClassificationEvidence(string Column,string Value);
public record Classification(string Status,string Basis,string Reason,List<ClassificationEvidence> Evidence);

public static class ScreeningClassifier
{
 public const string Version="check-rollup-v2";
 public static Classification Evaluate(IReadOnlyDictionary<string,string> fields,Mapping mapping)
 {
  // An explicitly mapped overall-result column remains authoritative. Its raw value is preserved.
  if(!string.IsNullOrWhiteSpace(mapping.Status))
  {
   var value=fields.GetValueOrDefault(mapping.Status,"");var normalized=ImportService.Classify(value);
   return new(normalized,"Source overall result",normalized=="UNCLASSIFIED"?"The mapped overall-result field is empty or unrecognized.":"Uses the explicitly mapped source overall result.",[new(mapping.Status,value)]);
  }
  var excluded=new[]{mapping.Id,mapping.Name,mapping.Role,mapping.Location,mapping.Remarks};
  var checks=fields.Where(p=>!excluded.Contains(p.Key)&&ImportService.IsCheck(p.Key)).Select(p=>new ClassificationEvidence(p.Key,p.Value)).ToList();
  if(checks.Count==0)return new("UNCLASSIFIED","Check roll-up", "No screening-check columns were recognized. Map an overall result or use recognizable check headers.",[]);
  var stop=checks.Where(p=>Regex.IsMatch(p.Value.Trim(),@"^(?:hard\s+stop|stop|reject(?:ed)?)(?:\s*[:;\-/–—]|$)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)).ToList();
  if(stop.Count>0)return new("STOP","Check roll-up",$"REJECT (STOP) queue: {stop.Count} check(s) explicitly labelled STOP, Hard Stop or Reject. STOP takes priority over REVIEW and PASS.",stop);
  var review=checks.Where(p=>Regex.IsMatch(p.Value.Trim(),@"^review(?:\s*[:;\-/–—]|$)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)).ToList();
  if(review.Count>0)return new("REVIEW","Check roll-up",$"{review.Count} check(s) explicitly labelled REVIEW, with no STOP/Hard Stop/Reject check.",review);
  var unresolved=checks.Where(p=>!p.Value.Trim().Equals("PASS",StringComparison.OrdinalIgnoreCase)).ToList();
  if(unresolved.Count>0)return new("REVIEW","Check roll-up",$"Administrative review: {unresolved.Count} check(s) are blank, unresolved references or unrecognized values. PASS requires every recognized screening check to explicitly say Pass.",unresolved);
  return new("PASS","Check roll-up",$"All {checks.Count} screening checks explicitly say Pass; no STOP or REVIEW check. Personal-information fields are excluded.",checks);
 }
}
