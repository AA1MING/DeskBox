using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DeskBox.Models;

public sealed class DeskItem
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public string Name { get; set; } = string.Empty;

	public ItemKind Kind { get; set; } = ItemKind.File;

	public string? StoredName { get; set; }

	public string? Target { get; set; }

	public string? Arguments { get; set; }

	public int ClickCount { get; set; }

	public DateTime AddedUtc { get; set; } = DateTime.UtcNow;

	public List<DeskItem> Children { get; set; } = new List<DeskItem>();

	[JsonIgnore]
	public bool IsGroup => Kind == ItemKind.Group;

	public DeskItem CloneShallow()
	{
		return new DeskItem
		{
			Id = Id,
			Name = Name,
			Kind = Kind,
			StoredName = StoredName,
			Target = Target,
			Arguments = Arguments,
			ClickCount = ClickCount,
			AddedUtc = AddedUtc,
			Children = Children
		};
	}
}
