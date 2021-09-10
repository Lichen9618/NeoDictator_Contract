using System;
using System.ComponentModel;
using System.Numerics;
using Neo;
using Neo.SmartContract;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;


namespace CoreContract
{
    [DisplayName("CoreContract")]
    [ManifestExtra("Author", "NEODictator")]
    [ManifestExtra("Email", "developer@neo.org")]
    [ManifestExtra("Description", "This is a NEODictator CoreContract")]
    public class HelloContract : SmartContract
    {
        private const byte daoAccountPrefix = 0x01;
        private const byte neoStakedAmountKey = 0x02;
        private const byte userStakePrefix = 0x03;
        private const byte voteContractPrefix = 0x04;
        private const byte currentVoteContractPrefix = 0x05;
        private const byte lastCalculationHeightKey = 0x06;
        private const byte lastGasProfitPerNeoKey = 0x07;
        private const byte historyProfitPrefix = 0x08;
        private const byte voteTargetWhiteListPrefix = 0x09;
        private const byte transactionFeeKey = 0x0A;

        //TODO:部署前确认初始的DAOAccount
        [InitialValue("Neo4N2sfMRuvMctC7Ej3tu4G4mDLaoxE7g", ContractParameterType.Hash160)]
        private static readonly UInt160 defaultDAOAccount = default;

        public static event Action<object> OnDeploy;
        public static event Action<object> Notify;

        #region userInterface
        public static bool Stake(UInt160 fromAddress, BigInteger StakeAmount) 
        {
            Require(StakeAmount > 0, "bad stakeAmount");
            //清算一次Gas收益
            Iterator VoteContracts = GetAllVoteContract();
            BigInteger gasIncreaseAmount = 0;
            while (VoteContracts.Next()) 
            {
                UInt160 voteContractHash = (UInt160)VoteContracts.Value;
                gasIncreaseAmount += (BigInteger)Contract.Call(voteContractHash, "claimGasByCore", CallFlags.All);
            }
            //记录此高度上的单位NEO产生的收益
            BigInteger increaseGasProfitPerNeo = gasIncreaseAmount / GetNeoStakeAmount();
            UpdateGasProfitPerNEO(increaseGasProfitPerNeo);

            //质押用户NEO并进行记录
            UInt160 currentVoteContract = GetVoteContractInternal();
            Require((bool)Contract.Call(NEO.Hash, "transfer", CallFlags.All, fromAddress, currentVoteContract, StakeAmount, null), "stake neo fail");
            AddUserStakeAmount(fromAddress, StakeAmount);

            WithDrawTransactionFee(fromAddress);
            UpdateNeoStakedAmount(StakeAmount);
            //使用新增NEO进行投票           
            return true;
        }

        public static bool Unstake(UInt160 fromAddress, BigInteger UnstakeAmount) 
        {
            Require(UnstakeAmount > 0, "bad unstakeAmount");
            //清算一次Gas收益,同时记录有足够多Gas的VoteContract
            UInt160 unstakeVoteContract = null;
            Iterator VoteContracts = GetAllVoteContract();
            BigInteger gasIncreaseAmount = 0;
            while (VoteContracts.Next())
            {
                UInt160 voteContractHash = (UInt160)VoteContracts.Value;
                gasIncreaseAmount += (BigInteger)Contract.Call(voteContractHash, "claimGasByCore", CallFlags.All);
                if (NEO.BalanceOf(voteContractHash) >= UnstakeAmount && unstakeVoteContract is null) 
                {
                    unstakeVoteContract = voteContractHash;
                }
            }
            //记录此高度上的单位NEO产生的收益
            BigInteger increaseGasProfitPerNeo = gasIncreaseAmount / GetNeoStakeAmount();
            UpdateGasProfitPerNEO(increaseGasProfitPerNeo);

            //更新质押记录并进行转账
            AddUserStakeAmount(fromAddress, -UnstakeAmount);
            UpdateNeoStakedAmount(-UnstakeAmount);
            //从子投票合约中回收，并转账至user  
            if (unstakeVoteContract is null)
            {
                VoteContracts = GetAllVoteContract();
                BigInteger unstakedAmount = 0;
                while (VoteContracts.Next()) 
                {
                    UInt160 voteContractHash = (UInt160)VoteContracts.Value;
                    BigInteger NEOBalance = NEO.BalanceOf(voteContractHash);
                    if (UnstakeAmount - unstakedAmount >= NEOBalance)
                    {
                        Require((bool)Contract.Call(unstakeVoteContract, "unstakeByCore", CallFlags.All, fromAddress, UnstakeAmount, null), "Unstake neo fail");
                        return true;
                    }
                    else 
                    {
                        Require((bool)Contract.Call(unstakeVoteContract, "unstakeByCore", CallFlags.All, fromAddress, NEOBalance, null), "Unstake neo fail");
                        unstakedAmount += NEOBalance;
                    }                   
                }
                throw new Exception("NEO not enough for Unstake");
            }
            else 
            {
                Require((bool)Contract.Call(unstakeVoteContract, "unstakeByCore", CallFlags.All, fromAddress, UnstakeAmount, null), "Unstake neo fail");
                WithDrawTransactionFee(fromAddress);
                return true;
            }
        }

        public static bool ClaimProfit(UInt160 claimAddress) 
        {
            Require(Runtime.CheckWitness(claimAddress), "check witness fail");
            StakeInfo Info = GetUserStakeInfoByAddress(claimAddress);
            if (Info.unclaimProfit > GAS.BalanceOf(Runtime.ExecutingScriptHash))
            {
                UInt160 unstakeVoteContract = null;
                Iterator VoteContracts = GetAllVoteContract();
                while (VoteContracts.Next())
                {
                    UInt160 voteContractHash = (UInt160)VoteContracts.Value;
                    if (GAS.BalanceOf(voteContractHash) >= Info.unclaimProfit)
                    {
                        unstakeVoteContract = voteContractHash;
                        break;
                    }
                }
                if (unstakeVoteContract is null)
                {
                    Iterator Contracts = GetAllVoteContract();
                    while (Contracts.Next())
                    {
                        UInt160 voteContractHash = (UInt160)VoteContracts.Value;
                        Require((bool)Contract.Call(voteContractHash, "claimByCore", CallFlags.All, Runtime.ExecutingScriptHash), "claim from vote fail");
                    }
                }
                else 
                {
                    Require((bool)Contract.Call(unstakeVoteContract, "claimByCore", CallFlags.All, Runtime.ExecutingScriptHash), "claim from vote fail");
                }                
            }
            Require((bool)Contract.Call(GAS.Hash, "transfer", CallFlags.All, Runtime.ExecutingScriptHash, claimAddress, Info.unclaimProfit, null), "claim gas fail");
            Info.unclaimProfit = 0;
            SetUserStakeInfo(claimAddress, Info);
            return true;
        }

        #endregion

        #region DAO
        public static void _deploy(object data, bool update)
        {
            Storage.Put(Storage.CurrentContext, new byte[] { daoAccountPrefix }, defaultDAOAccount);
            OnDeploy(defaultDAOAccount);
        }

        public static bool Update(ByteString nefFile, string manifest)
        {
            Require(Runtime.CheckWitness(GetDAOAccount()), "upgrade: CheckWitness failed!");
            ContractManagement.Update(nefFile, manifest);
            return true;
        }

        public static UInt160 GetDAOAccount() 
        {            
            return (UInt160)Storage.Get(Storage.CurrentContext, new byte[] { daoAccountPrefix });
        }

        public static bool SetNEOPosition(UInt160 ContractA, UInt160 ContractB, BigInteger amount) 
        {
            Require(Runtime.CheckWitness(GetDAOAccount()), "check DAO witeness fail");
            return (bool)Contract.Call(ContractA, "changeNEOPosition", CallFlags.All, ContractB, amount);
        }

        public static bool SetVoteTarget(UInt160 voteContract, Neo.Cryptography.ECC.ECPoint voteTo) 
        {
            Require(Runtime.CheckWitness(GetDAOAccount()), "check DAO witness fail");
            Contract.Call(voteContract, "changeVoteTargetByCore", CallFlags.All, voteTo);
            return true;
        }

        private static void WithDrawTransactionFee(UInt160 fromAddress) 
        {
            Require((bool)Contract.Call(GAS.Hash, "transfer", CallFlags.All, fromAddress, Runtime.ExecutingScriptHash, GetTransactionFee(), null), "withdraw fee fail");
        }

        [Safe]
        public static BigInteger GetTransactionFee() 
        {
            ByteString rawFee = Storage.Get(Storage.CurrentContext, new byte[] { transactionFeeKey });
            return rawFee is null ? 1000000 : (BigInteger)rawFee;
        }

        public static bool SetTransactionFee(BigInteger Fee) 
        {
            Require(Runtime.CheckWitness(GetDAOAccount()), "Check DAO witness fail");
            Storage.Put(Storage.CurrentContext, new byte[] { transactionFeeKey }, Fee);
            return true;
        }

        public static bool IsInVoteTargetWhiteList(UInt160 target) 
        {
            StorageMap map = VoteTargetWhiteListMap();
            Iterator allTargets = map.Find();
            while (allTargets.Next()) 
            {
                UInt160 WhiteList = (UInt160)allTargets.Value;
                if (WhiteList.Equals(target)) return true;
            }
            return false;
        }

        public static bool AdminVoteTargetWhiteList(BigInteger index, UInt160 voteTarget) 
        {
            Require(Runtime.CheckWitness(GetDAOAccount()), "Check DAO witness fail");
            StorageMap map = VoteContractStorageMap();
            map.Put(index.ToByteArray(), voteTarget);
            return true;
        }

        private static StorageMap VoteTargetWhiteListMap() 
        {
            return new StorageMap(Storage.CurrentContext, voteTargetWhiteListPrefix);
        }
        #endregion

        #region voteContractAdmin

        public static bool ClaimGasFromVoteContract() 
        {
            Runtime.CheckWitness(GetDAOAccount());
            return true;
        }

        public static Iterator GetAllVoteContract() 
        {
            StorageMap voteContractStorage = VoteContractStorageMap();
            return voteContractStorage.Find();
        }

        private static UInt160 GetVoteContractInternal() 
        {
            (UInt160 result, BigInteger Index) = GetCurrentVoteContract();
            Index++;
            Storage.Put(Storage.CurrentContext, new byte[] { currentVoteContractPrefix }, Index);
            return result;
        }

        [Safe]
        public static (UInt160, BigInteger)GetCurrentVoteContract()         
        {
            StorageMap voteContractStorage = VoteContractStorageMap();
            ByteString rawVoteContractIndex = Storage.Get(Storage.CurrentContext, new byte[] { currentVoteContractPrefix });
            BigInteger voteContractIndex = (rawVoteContractIndex is null) ? 0 : (BigInteger)rawVoteContractIndex;
            return ((UInt160)voteContractStorage.Get((ByteString)voteContractIndex), voteContractIndex);
        }

        private static StorageMap VoteContractStorageMap() 
        {
            return new StorageMap(Storage.CurrentContext, voteContractPrefix);
        }
        #endregion

        #region userStakeImplementation
        private static void AddUserStakeAmount(UInt160 fromAddress, BigInteger addAmount) 
        {
            StakeInfo Info = GetUserStakeInfoByAddress(fromAddress);
            //计算应有收益
            BigInteger historyValue = GetHistoryGasProfitPerNEO(Info.StakeHeight);
            BigInteger currentValue = GetLastGasProfitPerNeo();
            BigInteger profit = (currentValue - historyValue) * Info.StakeAmount;

            Info.StakeAmount += addAmount;
            Info.StakeHeight = Ledger.CurrentIndex;
            Info.unclaimProfit += profit;

            Require(Info.StakeAmount >= 0, "StakeAmount error");
            SetUserStakeInfo(fromAddress, Info);
        }

        private static void SetUserStakeInfo(UInt160 fromAddress, StakeInfo info) 
        {
            StorageMap userStakeMap = UserStakeMap();
            userStakeMap.Put(fromAddress, StdLib.Serialize(info));
        }

        private static StakeInfo GetUserStakeInfoByAddress(UInt160 fromAddress) 
        {
            StorageMap userStakeMap = UserStakeMap();
            ByteString rawInfo = userStakeMap.Get(fromAddress);
            if (rawInfo is null)
            {
                return new StakeInfo
                {
                    StakeAmount = 0,
                    StakeHeight = 0
                };
            }
            else 
            {
                return (StakeInfo)StdLib.Deserialize(rawInfo);
            }
        }

        private static StorageMap UserStakeMap()
        {
            return new StorageMap(Storage.CurrentContext, userStakePrefix);
        }
        #endregion

        #region globalState
        private static void UpdateNeoStakedAmount(BigInteger amount) 
        {
            ByteString rawAmount = Storage.Get(Storage.CurrentContext, new byte[] { neoStakedAmountKey });
            BigInteger stakedAmount = rawAmount is null ? 0 : (BigInteger)rawAmount;
            stakedAmount += amount;
            Require(stakedAmount >= 0, "stakedAmount error");
            Storage.Put(Storage.CurrentContext, new byte[] { neoStakedAmountKey }, stakedAmount);
        }

        public static BigInteger GetNeoStakeAmount() 
        {
            ByteString rawAmount = Storage.Get(Storage.CurrentContext, new byte[] { neoStakedAmountKey });
            return rawAmount is null ? 0 : (BigInteger)rawAmount;
        }

        [Safe]
        public static BigInteger GetLastCalculationHeight() 
        {
            ByteString rawHeight = Storage.Get(Storage.CurrentContext, new byte[] { lastCalculationHeightKey });
            return (rawHeight is null) ? 0 : (BigInteger)rawHeight;
        }
        
        /// <summary>
        /// 更新 最近单位NEO可获得收益
        /// 更新 当前高度NEO可获得收益
        /// </summary>
        /// <param name="increaseAmount"></param>
        private static void UpdateGasProfitPerNEO(BigInteger increaseAmount)
        {
            BigInteger value = GetLastGasProfitPerNeo() + increaseAmount;
            Storage.Put(Storage.CurrentContext, new byte[] { lastGasProfitPerNeoKey }, value);
            GetHistoryProfitMap().Put(((ByteString)(BigInteger)Ledger.CurrentIndex), value);
        }

        [Safe]
        public static BigInteger GetHistoryGasProfitPerNEO(uint Height) 
        {
            ByteString rawResult = GetHistoryProfitMap().Get((ByteString)(BigInteger)Height);
            return (rawResult is null) ? 0 : (BigInteger)rawResult;
        }

        [Safe]
        public static BigInteger GetLastGasProfitPerNeo()
        {
            ByteString rawProfit = Storage.Get(Storage.CurrentContext, new byte[] { lastGasProfitPerNeoKey });
            return (rawProfit is null) ? 0 : (BigInteger)rawProfit;
        }

        private static StorageMap GetHistoryProfitMap() 
        {
            StorageMap result = new StorageMap(Storage.CurrentContext, historyProfitPrefix);
            return result;
        }
        #endregion

        #region Helper
        private static void Require(bool condition, string info) 
        {
            if (condition == false) 
            {
                Notify(info);
                throw new Exception();
            }
        }
        #endregion

        #region struct
        struct StakeInfo 
        {
            public BigInteger StakeAmount;
            public uint StakeHeight;
            public BigInteger unclaimProfit;
        }
        #endregion
    }
}
