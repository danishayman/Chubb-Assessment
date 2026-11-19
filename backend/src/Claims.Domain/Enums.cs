namespace Claims.Domain;

public enum UserRole { Claimant, ClaimsOfficer, Manager }

public enum ClaimType { Motor, Property }

public enum Market { SG, MY, HK, TH, ID, AU }

public enum ClaimStatus { Submitted, UnderReview, AwaitingInformation, Approved, Rejected, Settled }

public enum InfoRequestStatus { Open, Answered }
