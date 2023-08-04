namespace GSANotifications.Models
{
    public class NotificationChangeRequest
    {
        // Actions taken through dashboards
        public const string revokeAuditAssignment = "revoked audit";
        public const string acknowledged = "acknowledged";
        public const string acceptedAuditAssignment = "accepted audit";
        public const string declinedAuditAssignment = "declined audit"; 
        
        public string NotificationId { get; set; } = string.Empty;
        public string AuditId { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;        
        public string RequestedBy { get; set; } = string.Empty;
        public bool SendAcknowledgableNotification { get; set; } = true;
        public string NotifyAccountId { get; set; } = string.Empty;
        public string AccountId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;        
    }
}