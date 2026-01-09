namespace Common.Models
{
    public class MessageResponse
    {
        public string Data
        {
            get;
            set;
        }

        public string Status
        {
            get;
            set;
        }

        public MessageResponse(string status, string data)
        {
            Status = status;
            Data = data;
        }
    }
}