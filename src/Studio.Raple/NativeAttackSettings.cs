using System.Reflection;
using Studio.Core;

namespace Studio.Raple;
public sealed record NativeAttackSettings(string Kind,IReadOnlyDictionary<string,double> Values,IReadOnlyDictionary<string,bool> Flags,IReadOnlyDictionary<string,string> Enums)
{
    public double this[string name]=>Values[name];
}
public sealed partial class RapleAdapter
{
    NativeAttackSettings? ReadNativeAttackSettings(BehaviorDocument document,BehaviorNode node,string kind)
    {
        var values=new Dictionary<string,double>();var flags=new Dictionary<string,bool>();var enums=new Dictionary<string,string>();var args=new List<object>();
        foreach(var parameter in Constructor(RuntimeType(kind)).GetParameters())
        {
            if(parameter.ParameterType==typeof(bool))
            {if(!ReadConstantParameter(document,node,parameter.Name!,parameter.DefaultValue,out var value)||value is not bool flag)return null;flags[parameter.Name!]=flag;args.Add(flag);continue;}
            if(parameter.ParameterType.IsEnum)
            {
                if(!ReadConstantParameter(document,node,parameter.Name!,parameter.HasDefaultValue?parameter.DefaultValue:null,out var value)||value==null)return null;
                try{var number=Convert.ToInt32(value,System.Globalization.CultureInfo.InvariantCulture);var enumValue=Enum.ToObject(parameter.ParameterType,number);if(!Enum.IsDefined(parameter.ParameterType,enumValue))return null;values[parameter.Name!]=number;args.Add(enumValue);enums[parameter.Name!]=enumValue.ToString()!;}catch(Exception e)when(e is InvalidCastException or FormatException or OverflowException){return null;}continue;
            }
            var fallback=parameter.HasDefaultValue?Convert.ToDouble(parameter.DefaultValue,System.Globalization.CultureInfo.InvariantCulture):(double?)null;
            if(!ReadNumericParameter(document,node,parameter.Name!,fallback,out var numeric)||numeric==null||!double.IsFinite(numeric.Value))return null;
            try{if(parameter.ParameterType!=typeof(double)&&numeric!=Math.Truncate(numeric.Value))return null;args.Add(Convert.ChangeType(numeric.Value,parameter.ParameterType,System.Globalization.CultureInfo.InvariantCulture));values[parameter.Name!]=numeric.Value;}catch(Exception e)when(e is InvalidCastException or OverflowException){return null;}
        }
        try{Constructor(RuntimeType(kind)).Invoke(args.ToArray());}catch(TargetInvocationException){return null;}
        return new(kind,values,flags,enums);
    }
}
