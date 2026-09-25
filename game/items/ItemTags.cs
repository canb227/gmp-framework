using PolyType;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


/// <summary>
/// Properties of a physical item that drive interactions between items (see <see cref="TagInteractions"/>).
/// Stored in scenes by value, so never renumber existing entries.
/// </summary>
public enum ItemTags
{
    INERT = 0,
    HOT = 1,
    COLD = 2,
}