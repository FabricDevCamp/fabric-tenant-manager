using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

public class Variable {
  [JsonPropertyName("name")]
  public string Name { get; set; }
  [JsonPropertyName("value")]
  public object Value { get; set; }
  [JsonPropertyName("type")]
  public string Type { get; set; }
  [JsonPropertyName("note")]
  public string Note { get; set; }

  public Variable(string name, object value, string variableType = "String", string note = "") {
    Name = name;
    Value = value;
    Type = variableType;
    Note = note;
  }

  public string Encode() {
    return JsonSerializer.Serialize(this);
  }
}

public class VariableOverride {
  [JsonPropertyName("name")]
  public string Name { get; set; }

  [JsonPropertyName("value")]
  public object Value { get; set; }

  public VariableOverride(string name, object value) {
    Name = name;
    Value = value;
  }

  public string Encode() {
    return JsonSerializer.Serialize(this);
  }
}

public class Valueset {
  [JsonPropertyName("name")]
  public string Name { get; set; }

  [JsonPropertyName("variableOverrides")]
  public List<VariableOverride> VariableOverrides { get; set; }

  public Valueset(string name) {
    Name = name;
    VariableOverrides = new List<VariableOverride>();
  }

  public string Encode() {
    return JsonSerializer.Serialize(this);
  }

  public void AddVariableOverride(string name, string value) {
    VariableOverrides.Add(new VariableOverride(name, value));
  }

  public void AddConnectionVariableOverride(string name, string connectionId) {
    var value = new Dictionary<string, string>
    {
            { "connectionId", connectionId }
        };
    VariableOverrides.Add(new VariableOverride(name, value));
  }

  public void AddItemVariableOverride(string name, dynamic item) {
    var value = new Dictionary<string, string>  {
      { "itemId", item.Id },
      { "workspaceId", item.WorkspaceId }
    };
    VariableOverrides.Add(new VariableOverride(name, value));
  }
}

public class VariableLibraryFactory {

  public string Name { get; set; }
  public List<Variable> Variables { get; set; }
  public List<Valueset> Valuesets { get; set; }
  public List<string> ValueSetsOrder { get; set; }

  public VariableLibraryFactory(string name, List<Variable> variables = null, List<Valueset> valuesets = null) {
    Name = name;
    Variables = variables ?? new List<Variable>();
    Valuesets = valuesets ?? new List<Valueset>();
    ValueSetsOrder = new List<string>();
  }

  public Dictionary<string, object> Encode() {
    return new Dictionary<string, object> {
      { "variables", Variables },
      { "valuesets", Valuesets },
      { "valueSetsOrder", ValueSetsOrder }
    };
  }

  public void AddVariable(string name, object value, string variableType = "String", string note = "some note") {
    Variables.Add(new Variable(name, value, variableType, note));
  }

  public void AddConnectionVariable(string name, string connectionId, string note = "some note") {
    var value = new Dictionary<string, string> { { "connectionId", connectionId } };
    Variables.Add(new Variable(name, value, "ConnectionReference", note));
  }

  public void AddItemVariable(string name, dynamic item, string note = "some note") {
    var value = new Dictionary<string, object> {
      { "itemId", item.id },
      { "workspaceId", item.workspace_id }
    };
    Variables.Add(new Variable(name, value, "ItemReference", note));
  }

  public void AddValueset(Valueset valueset) {
    Valuesets.Add(valueset);
    ValueSetsOrder.Add(valueset.Name);
  }

  public Valueset GetValueset(string valuesetName) {
    return Valuesets.FirstOrDefault(valueset => valueset.Name == valuesetName);
  }

  public string GetVariableJson() {
    var variableLib = new Dictionary<string, object> {
      { "$schema", "https://developer.microsoft.com/json-schemas/fabric/item/variableLibrary/definition/variables/1.0.0/schema.json" },
      { "variables", Variables }
    };

    var options = new JsonSerializerOptions { WriteIndented = true };
    return JsonSerializer.Serialize(variableLib, options);
  }

  public string GetValuesetJson(string valuesetName) {
    var valueset = Valuesets.First(v => v.Name == valuesetName);

    var valuesetExport = new Dictionary<string, object> {
      { "$schema", "https://developer.microsoft.com/json-schemas/fabric/item/variableLibrary/definition/valueSet/1.0.0/schema.json" },
      { "name", valueset.Name },
      { "variableOverrides", valueset.VariableOverrides }
    };

    var options = new JsonSerializerOptions { WriteIndented = true };
    return JsonSerializer.Serialize(valuesetExport, options);
  }
}
