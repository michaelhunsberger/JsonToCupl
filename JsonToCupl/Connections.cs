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

        public PinConnection GetOutputOrBidirectional()
        {
            var outputs = this.Where(c =>
                c.DirectionType == DirectionType.Output || c.DirectionType == DirectionType.Bidirectional);
            var outputsArr = outputs.ToArray();
            if (outputsArr.Length == 0)
                return null;
            if (outputsArr.Length == 1)
                return outputsArr[0];
            throw new ApplicationException("Ambiguous number of output pin connections found");
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
