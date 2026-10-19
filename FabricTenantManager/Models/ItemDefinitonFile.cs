using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

public class ItemDefinitonFile {
  public string FullPath { get; set; }
  public string Content { get; set; }

  public string ItemName {
    get {
      return FullPath.Contains("/") ? FullPath.Substring(0, FullPath.IndexOf("/")) : FullPath;
    }
  }

  public string Path {
    get {
      int firstSlash = FullPath.IndexOf("/");
      if (firstSlash == -1) {
        return FullPath;
      }
      else {
        int start = firstSlash + 1;
        int length = FullPath.Length - start;
        return FullPath.Substring(start, length);

      }
    }
  }

  public string FileName {
    get {
      return FullPath.Substring(FullPath.LastIndexOf('/') + 1);
    }
  }

}

public class PlatformFileConfig {
  public string version { get; set; }
  public string logicalId { get; set; }
}

public class PlatformFileMetadata {
  public string type { get; set; }
  public string displayName { get; set; }
}

public class FabricPlatformFile {
  [JsonPropertyName("$schema")]
  public string schema { get; set; }
  public PlatformFileMetadata metadata { get; set; }
  public PlatformFileConfig config { get; set; }
}
