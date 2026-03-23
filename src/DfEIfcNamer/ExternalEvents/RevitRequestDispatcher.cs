using System;
using Autodesk.Revit.UI;

namespace DfEIfcNamer.ExternalEvents
{
    public class RevitRequestDispatcher
    {
        private readonly RevitExecutionHandler _handler;
        private readonly ExternalEvent _event;

        public RevitRequestDispatcher(RevitExecutionHandler handler, ExternalEvent externalEvent)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _event = externalEvent ?? throw new ArgumentNullException(nameof(externalEvent));
        }

        public void Raise(RevitRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            _handler.SetRequest(request);
            _event.Raise();
        }
    }
}
