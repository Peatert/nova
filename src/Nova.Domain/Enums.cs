namespace Nova.Domain;

/// <summary>Technical — внутренние счета банка (корсчёт и т.п.), через них идут внешние поступления и списания.</summary>
public enum AccountType { Debit, Savings, Credit, Technical }

public enum AccountStatus { Active, Frozen, Closed }

public enum CardStatus { Active, Blocked, Expired }

public enum TransferStatus { Pending, Completed, Failed }

public enum EntryDirection { Debit, Credit }

public enum ActorType { Client, Staff, System }
