//===-----------------------------------------------------------------------==//
//
//                GPUVerify - a Verifier for GPU Kernels
//
// This file is distributed under the Microsoft Public License.  See
// LICENSE.TXT for details.
//
//===----------------------------------------------------------------------===//

using System.Collections.Generic;
using System;
using System.Diagnostics;
using Microsoft.Boogie;

namespace GPUVerify
{
  public class GPUVerifyCruncherCommandLineOptions : GVCommandLineOptions
  {
    // Assume a sequential pipeline unless the user selects otherwise
    public Pipeline Pipeline = new Pipeline(sequential: true);

    public bool WriteKilledInvariantsToFile = false;
    public bool ReplaceLoopInvariantAssertions = false;
    public bool EnableBarrierDivergenceChecks = false;
    public string PipelineString = null;

    public GPUVerifyCruncherCommandLineOptions() :
      base()
    {
    }

    protected override bool ParseOption(string name, CommandLineParseState ps)
    {
      if (name == "sequential") {
        if (ps.ConfirmArgumentCount(1)) {
          Debug.Assert(PipelineString == null);
          PipelineString = ps.args[ps.i];
        }
        return true;
      }

      if (name == "parallel") {
        if (ps.ConfirmArgumentCount(1)) {
          Pipeline.Sequential = false;
          Debug.Assert(PipelineString == null);
          PipelineString = ps.args[ps.i];
        }
        return true;
      }

      if (name == "noHoudini") {
        Pipeline.runHoudini = false;
        return true;
      }

      if (name == "writeKilledInvariantsToFile") {
        WriteKilledInvariantsToFile = true;
        return true;
      }

      if (name == "replaceLoopInvariantAssertions") {
        ReplaceLoopInvariantAssertions = true;
        return true;
      }

      if (name == "enableBarrierDivergenceChecks") {
        EnableBarrierDivergenceChecks = true;
        return true;
      }

      return base.ParseOption(name, ps);
    }

    internal void ParsePipelineString ()
    {
      if(PipelineString == null) {
        return;
      }
      const char lhsDelimiter = '[';
      const char rhsDelimiter = ']';
      const char engineDelimiter = '-';
      Debug.Assert (PipelineString[0] == lhsDelimiter && PipelineString[PipelineString.Length - 1] == rhsDelimiter);
      string[] engines = PipelineString.Substring(1, PipelineString.Length - 2).Split(engineDelimiter);
      foreach (string engineStr in engines)
      {
        int lhsDelimiterIdx = engineStr.IndexOf(lhsDelimiter);
        string engine;
        if (lhsDelimiterIdx != -1)
        {
          engine = engineStr.Substring(0, lhsDelimiterIdx);
        }
        else
        {
          engine = engineStr;
        }
        if (engine.ToUpper().Equals(VanillaHoudini.Name))
        {
          // The user wants to override Houdini settings used in the cruncher

          string parameterStr = engineStr.Substring(VanillaHoudini.Name.Length);
          Dictionary<string, string> parameters = GetParameters(VanillaHoudini.Name,
                                                                VanillaHoudini.GetAllowedParameters(),
                                                                VanillaHoudini.GetRequiredParameters(),
                                                                parameterStr);
          CheckForMutuallyExclusiveParameters(VanillaHoudini.Name,
                                              VanillaHoudini.GetMutuallyExclusiveParameters(),
                                              parameters);

          int errorLimit = ParseIntParameter(parameters,
                                             SMTEngine.GetErrorLimitParameter().Name,
                                             SMTEngine.GetErrorLimitParameter().DefaultValue);
          VanillaHoudini houdiniEngine = new VanillaHoudini(Pipeline.GetNextSMTEngineID(),
                                                            GetSolverValue(parameters),
                                                            errorLimit);
          Pipeline.AddEngine(houdiniEngine);
          houdiniEngine.Delay = ParseIntParameter(parameters,
                                                  VanillaHoudini.GetDelayParameter().Name,
                                                  VanillaHoudini.GetDelayParameter().DefaultValue);
          houdiniEngine.SlidingSeconds = ParseIntParameter(parameters,
                                                           VanillaHoudini.GetSlidingSecondsParameter().Name,
                                                           VanillaHoudini.GetSlidingSecondsParameter().DefaultValue);
          houdiniEngine.SlidingLimit = ParseIntParameter(parameters,
                                                         VanillaHoudini.GetSlidingLimitParameter().Name,
                                                         VanillaHoudini.GetSlidingLimitParameter().DefaultValue);
        }
        else if (engine.ToUpper().Equals(SBASE.Name))
        {
          string parameterStr = engineStr.Substring(SBASE.Name.Length);
          Dictionary<string, string> parameters = GetParameters(SBASE.Name,
                                                                SBASE.GetAllowedParameters(),
                                                                SBASE.GetRequiredParameters(),
                                                                parameterStr);
          int errorLimit = ParseIntParameter(parameters,
                                             SMTEngine.GetErrorLimitParameter().Name,
                                             SMTEngine.GetErrorLimitParameter().DefaultValue);
          Pipeline.AddEngine(new SBASE(Pipeline.GetNextSMTEngineID(),GetSolverValue(parameters),errorLimit));
        }
        else if (engine.ToUpper().Equals(SSTEP.Name))
        {
          string parameterStr = engineStr.Substring(SSTEP.Name.Length);
          Dictionary<string, string> parameters = GetParameters(SSTEP.Name,
                                                                SSTEP.GetAllowedParameters(),
                                                                SSTEP.GetRequiredParameters(),
                                                                parameterStr);
          int errorLimit = ParseIntParameter(parameters,
                                             SMTEngine.GetErrorLimitParameter().Name,
                                             SMTEngine.GetErrorLimitParameter().DefaultValue);
          Pipeline.AddEngine(new SSTEP(Pipeline.GetNextSMTEngineID(),GetSolverValue(parameters),errorLimit));
        }
        else if (engine.ToUpper().Equals(LU.Name))
        {
          string parameterStr = engineStr.Substring(LU.Name.Length);
          Dictionary<string, string> parameters = GetParameters(LU.Name,
                                                                LU.GetAllowedParameters(),
                                                                LU.GetRequiredParameters(),
                                                                parameterStr);
          int errorLimit = ParseIntParameter(parameters,
                                             SMTEngine.GetErrorLimitParameter().Name,
                                             SMTEngine.GetErrorLimitParameter().DefaultValue);
          Pipeline.AddEngine(new LU(Pipeline.GetNextSMTEngineID(),
                                    GetSolverValue(parameters),
                                    errorLimit,
                                    ParseIntParameter(parameters, LU.GetUnrollParameter().Name, 1)));
        }
        else if (engine.ToUpper().Equals(DynamicAnalysis.Name))
        {
          string parameterStr = engineStr.Substring(DynamicAnalysis.Name.Length);
          Dictionary<string, string> parameters = GetParameters(DynamicAnalysis.Name,
                                                                DynamicAnalysis.GetAllowedParameters(),
                                                                DynamicAnalysis.GetRequiredParameters(), parameterStr);
          DynamicAnalysis dynamicEngine = new DynamicAnalysis();
          dynamicEngine.LoopHeaderLimit = ParseIntParameter(parameters,
                                                            DynamicAnalysis.GetLoopHeaderLimitParameter().Name,
                                                            DynamicAnalysis.GetLoopHeaderLimitParameter().DefaultValue);
          dynamicEngine.LoopEscape = ParseIntParameter(parameters,
                                                       DynamicAnalysis.GetLoopEscapingParameter().Name,
                                                       DynamicAnalysis.GetLoopEscapingParameter().DefaultValue);
          dynamicEngine.TimeLimit = ParseIntParameter(parameters,
                                                      DynamicAnalysis.GetTimeLimitParameter().Name,
                                                      DynamicAnalysis.GetTimeLimitParameter().DefaultValue);
          Pipeline.AddEngine(dynamicEngine);
        }
        else
        {
          Console.WriteLine(string.Format("Unknown cruncher engine: '{0}'", engine));
          System.Environment.Exit((int)ToolExitCodes.OTHER_ERROR);
        }
      }
    }

    private Dictionary<string, string> GetParameters(string engine,
                                                     List<EngineParameter> allowedParams,
                                                     List<EngineParameter> requiredParams,
                                                     string parameterStr)
    {
      Dictionary<string, string> map = new Dictionary<string, string>();
      if (parameterStr.Length > 0)
      {
        Debug.Assert(parameterStr[0] == '[' && parameterStr[parameterStr.Length - 1] == ']');
        string[] parameters = parameterStr.Substring(1, parameterStr.Length - 2).Split(',');
        foreach (string param in parameters)
        {
          string[] values = param.Split('=');
          Debug.Assert(values.Length == 2);
          string paramName = values[0];
          if (allowedParams.Find(item => item.Name.Equals(paramName)) == null)
          {
            Console.WriteLine(string.Format("Parameter '{0}' is not valid for cruncher engine '{1}'", paramName, engine));
            System.Environment.Exit((int)ToolExitCodes.OTHER_ERROR);
          }
          map[paramName] = values[1].ToLower();
        }
      }
      foreach (EngineParameter param in requiredParams)
      {
        if (!map.ContainsKey(param.Name))
        {
          Console.WriteLine(string.Format("For cruncher engine '{0}' you must supply parameter '{1}'", engine, param.Name));
          System.Environment.Exit((int)ToolExitCodes.OTHER_ERROR);
        }
      }
      return map;
    }

    private void CheckForMutuallyExclusiveParameters(string engine,
                                                     List<Tuple<EngineParameter, EngineParameter>> mutuallyExclusivePairs,
                                                     Dictionary<string, string> parameters)
    {
      foreach (var tuple in mutuallyExclusivePairs)
      {
        if (parameters.ContainsKey(tuple.Item1.Name) && parameters.ContainsKey(tuple.Item2.Name))
        {
          Console.WriteLine(string.Format("Parameters '{0}' and '{1}' are mutually exclusive in cruncher engine '{2}'",
            tuple.Item1.Name, tuple.Item2.Name, engine));
          System.Environment.Exit((int)ToolExitCodes.OTHER_ERROR);
        }
      }
    }

    private string GetSolverValue(Dictionary<string, string> parameters)
    {
      if (parameters.ContainsKey(SMTEngine.GetSolverParameter().Name))
      {
        if (!SMTEngine.GetSolverParameter().IsValidValue(parameters[SMTEngine.GetSolverParameter().Name]))
        {
          Console.WriteLine(string.Format("Unknown solver '{0}'", parameters[SMTEngine.GetSolverParameter().Name]));
          System.Environment.Exit((int)ToolExitCodes.OTHER_ERROR);
        }
        return parameters[SMTEngine.GetSolverParameter().Name];
      }
      return SMTEngine.GetSolverParameter().DefaultValue;
    }

    private int ParseIntParameter(Dictionary<string, string> parameters, string paramName, int defaultValue)
    {
      if (!parameters.ContainsKey(paramName))
      {
        return defaultValue;
      }
      try
      {
        return Convert.ToInt32(parameters[paramName]);
      }
      catch (FormatException)
      {
        Console.WriteLine(string.Format("'{0}' must be an integer. You gave '{1}'", paramName, parameters[paramName]));
        System.Environment.Exit((int)ToolExitCodes.OTHER_ERROR);
      }
      catch (OverflowException)
      {
        Console.WriteLine(string.Format("'{0}' must fit into a 32-bit integer. You gave '{1}'", paramName, parameters[paramName]));
        System.Environment.Exit((int)ToolExitCodes.OTHER_ERROR);
      }
      return -1;
    }
  }

}
