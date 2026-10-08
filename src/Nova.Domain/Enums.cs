namespace Nova.Domain;

public enum AccountType { Debit, Savings, Credit }

public enum AccountStatus { Active, Frozen, Closed }

public enum CardStatus { Active, Blocked, Expired }

public enum TransferStatus { Pending, Completed, Failed }

public enum EntryDirection { Debit, Credit }

public enum ActorType { Client, Staff, System }
