//===-----------------------------------------------------------------------==//
//
//                GPUVerify - a Verifier for GPU Kernels
//
// This file is distributed under the Microsoft Public License.  See
// LICENSE.TXT for details.
//
//===----------------------------------------------------------------------===//

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Microsoft.Boogie;

namespace GPUVerify {

  public class SourceLocationInfo {

    public class Record {
      private int line;
      private int column;
      private string file;
      private string directory;

      public Record(int line, int column, string file, string directory) {
        this.line = line;
        this.column = column;
        this.file = file;
        this.directory = directory;
      }

      public int GetLine() {
        return line;
      }

      public int GetColumn() {
        return column;
      }

      public string GetFile() {
        return file;
      }

      public string GetDirectory() {
        return directory;
      }

      public override string ToString()
      {
        return GetFile() + ":" + GetLine() + ":" + GetColumn();
      }

      public override bool Equals(object obj) {
        if(!(obj is Record)) {
          return false;
        }
        Record ThatRecord = obj as Record;
        return line == ThatRecord.line &&
               column == ThatRecord.column &&
               file == ThatRecord.file &&
               directory == ThatRecord.directory;
      }

      public override int GetHashCode() {
        // A naive hash code function
        return line + column + file.GetHashCode() + directory.GetHashCode();
      }

    }

    private List<Record> records;

    public class RecordComparison : IComparer<Record> {
      public int Compare(Record s1, Record s2) {

        int directories = s1.GetDirectory().CompareTo(s2.GetDirectory());
        if(directories != 0) {
          return directories;
        }

        int files = s1.GetFile().CompareTo(s2.GetFile());
        if(files != 0) {
          return files;
        }

        if(s1.GetLine() < s2.GetLine()) {
          return -1;
        }

        if(s1.GetLine() > s2.GetLine()) {
          return 1;
        }

        if(s1.GetColumn() < s2.GetColumn()) {
          return -1;
        }

        if(s1.GetColumn() > s2.GetColumn()) {
          return 1;
        }

        return 0;
      }

    }

    public class SourceLocationInfoComparison : IComparer<SourceLocationInfo> {
      public int Compare(SourceLocationInfo s1, SourceLocationInfo s2) {
        foreach(var recordPair in s1.records.Zip(s2.records)) {
          int result = new RecordComparison().Compare(recordPair.Item1, recordPair.Item2);
          if(result != 0) {
            return result;
          }
        }
        if (s1.records.Count() < s2.records.Count()) {
          return -1;
        }
        if (s1.records.Count() > s2.records.Count()) {
          return 1;
        }
        return 0;
      }
    }

    public SourceLocationInfo(QKeyValue attributes, string sourceFileName, IToken fallBackToken) {
      records = new List<Record>();
      try {
        var sourceLocFileName = Path.Combine(
          Path.GetDirectoryName(sourceFileName),
          Path.GetFileNameWithoutExtension(sourceFileName) + ".loc");
        using (StreamReader sr = new StreamReader(sourceLocFileName)) {
          int number = QKeyValue.FindIntAttribute(attributes, "sourceloc_num", -1);
          if(number == -1) {
            throw new Exception();
          }
          var info = sr.ReadLine().Split(new char[] { '\x1D' })[number];
          var chain = info.Split(new char[] { '\x1E' });
          Record blangRecord = null;
          foreach(var c in chain) {
            if(c != "") {
              var sourceInfo = c.Split(new char[] { '\x1F' });
              int line = Convert.ToInt32(sourceInfo[0]);
              int column = Convert.ToInt32(sourceInfo[1]);
              string file = sourceInfo[2];
              string directory = sourceInfo[3];
              if(file.Contains("include-blang")) {
                // Do not keep source info if it is in one of our special header files
                blangRecord = new Record(line, column, file, directory);
                continue;
              }
              records.Add(new Record(line, column, file, directory));
            }
          }
          if (records.Count() == 0)
            records.Add(blangRecord);
        }
      } catch (Exception e) {
        Console.Error.WriteLine("warning: getting source loc info failed with: " + e.Message);
        records.Add(new Record(fallBackToken.line, fallBackToken.col, fallBackToken.filename, ""));
      }
    }

    private string FetchCodeLine(int i) {
      if(File.Exists(records[i].GetFile())) {
        return FetchCodeLine(records[i].GetFile(), records[i].GetLine());
      }
      return FetchCodeLine(Path.Combine(records[i].GetDirectory(), Path.GetFileName(records[i].GetFile())), records[i].GetLine());
    }

    public static string FetchCodeLine(string path, int lineNo) {
      try {
        TextReader tr = new StreamReader(path);
        string line = null;
        for (int currLineNo = 1; ((line = tr.ReadLine()) != null); currLineNo++) {
          if (currLineNo == lineNo) {
            return line;
          }
        }
        throw new Exception();
      }
      catch (Exception) {
        return "<unknown line of code>";
      }
    }

    public int Count() {
      return records.Count();
    }

    public Record Top() {
      return records[0];
    }

    public override string ToString()
    {
      // We don't want this to be invoked
      throw new NotImplementedException();
    }

    public void PrintStackTrace() {
      GVUtil.IO.ErrorWriteLine(TrimLeadingSpaces(FetchCodeLine(0), 2));
      for(int i = 1; i < Count(); i++) {
        Console.Error.WriteLine("invoked from " + records[i] + ":");
        GVUtil.IO.ErrorWriteLine(TrimLeadingSpaces(FetchCodeLine(i), 2));
      }
      Console.Error.WriteLine();
    }

    private static string TrimLeadingSpaces(string s1, int noOfSpaces) {
      if (string.IsNullOrWhiteSpace(s1)) {
        return s1;
      }

      int index;
      for (index = 0; (index + 1) < s1.Length && char.IsWhiteSpace(s1[index]); ++index) ;
      string returnString = s1.Substring(index);
      for (int i = noOfSpaces; i > 0; --i) {
        returnString = " " + returnString;
      }
      return returnString;
    }

    public override bool Equals(object obj) {
      if(!(obj is SourceLocationInfo)) {
        return false;
      }
      SourceLocationInfo ThatSourceLocationInfo = obj as SourceLocationInfo;
      if(this.records.Count() != ThatSourceLocationInfo.records.Count()) {
        return false;
      }
      foreach(var Pair in this.records.Zip(ThatSourceLocationInfo.records)) {
        if(!Pair.Item1.Equals(Pair.Item2)) {
          return false;
        }
      }
      return true;
    }

    public override int GetHashCode() {
      // A naive hashcode function
      int result = 0;
      foreach(var record in records) {
        result += record.GetHashCode();
      }
      return result;
    }

  }

}
