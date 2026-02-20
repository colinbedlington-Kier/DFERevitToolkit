using Autodesk.Revit.UI;

namespace DfEIfcNamer.ExternalEvents
{
    public class RevitRequestDispatcher
    {
        private readonly RevitExecutionHandler _handler;
        private readonly ExternalEvent _event;

        public RevitRequestDispatcher(RevitExecutionHandler handler, ExternalEvent externalEvent)
        {
            _handler = handler;
            _event = externalEvent;
        }

        public void Raise(RevitRequest request)
        {
            _handler.SetRequest(request);
            _event.Raise();
        }
    }
}
