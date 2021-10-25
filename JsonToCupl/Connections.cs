using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JsonToCupl
{
    class Connections : List<PinConnection>
    {
        public PinConnection GetOutput()
        {
            return this.FirstOrDefault(c => c.DirectionType == DirectionType.Output);
        }

        public IEnumerable<PinConnection> GetInputs()
        {
            return this.Where(c => c.DirectionType == DirectionType.Input);
        }

        public IEnumerable<PinConnection> GetInputsOrBidirectional()
        {
            return this.FindAll(c => c.DirectionType == DirectionType.Input || c.DirectionType == DirectionType.Bidirectional);
        }
    }
}
