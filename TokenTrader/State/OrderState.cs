using System;

namespace TokenTrader.State
{
    class OrderState
    {
        public decimal SpreadOneSidePerc { get; private set; }
        public decimal VolumeOneSideFiat { get; private set; }
        public string CurrentOrderId { get; private set; } = "";
        public LocalOrderStatus LocalStatus { get; private set; } = LocalOrderStatus.None;
        public DateTime LastUpdateTimestamp { get; private set; } = DateTime.MinValue;

        double MsSinceLastUpdate => (DateTime.UtcNow - LastUpdateTimestamp).TotalMilliseconds;

        public bool CanAddNew(int waitPendingTimeoutMs)
        {
            //заявок пока нет. можно выставлять.
            if (LocalStatus == LocalOrderStatus.None) return true;

            //удаление зависло или не пришло подтверждение, можно попробовать перевыставить. не пытаемся перевыставлять повисшее добавление
            if (LocalStatus == LocalOrderStatus.CancelPending && MsSinceLastUpdate >= waitPendingTimeoutMs) return true;

            return false;
        }

        public bool CanCancel(int waitPendingTimeoutMs)
        {
            //чтобы там ни происходило, orderId пустой. снимать всё равно нечего.
            if (CurrentOrderId == "") return false;

            //активная заявка. снимаем.
            if (LocalStatus == LocalOrderStatus.Active || LocalStatus == LocalOrderStatus.PartiallyExecuted) return true;

            //добавление зависло или не пришло подтверждение, можно попробовать снять ещё раз. не пытаемся снять повисшее снятие
            if (LocalStatus == LocalOrderStatus.AddPending && MsSinceLastUpdate >= waitPendingTimeoutMs) return true;

            return false;
        }

        public void InitialSet(string orderId, decimal spreadOneSidePerc, decimal volumeOneSideFiat, LocalOrderStatus status)
        {
            CurrentOrderId = orderId;
            SpreadOneSidePerc = spreadOneSidePerc;
            VolumeOneSideFiat = volumeOneSideFiat;
            Update(status);
        }

        public void SetNewOrderId(string orderId, LocalOrderStatus status)
        {
            CurrentOrderId    = orderId;
            Update(status);
        }

        public void Update(LocalOrderStatus status)
        {
            if (CurrentOrderId == "") return; //не апдейтим в случае, если заявка была уже удалена, а потом начали доезжать апдейты

            LocalStatus         = status;
            LastUpdateTimestamp = DateTime.UtcNow;

            if (status == LocalOrderStatus.None) CurrentOrderId = "";
        }

        //public void SetAddPending() => LocalStatus = LocalOrderStatus.AddPending;

        //public void AddNewOrder(OrderMessage order)
        //{
        //    Order       = order;
        //    LocalStatus = LocalOrderStatus.Active;
        //}

        //public void SetCancelPending() => LocalStatus = LocalOrderStatus.CancelPending;

        //public void SetPartiallyExecuted() => LocalStatus = LocalOrderStatus.PartiallyExecuted;

        //public void RemoveOrder()
        //{
        //    Order       = null;
        //    LocalStatus = LocalOrderStatus.None;
        //}
    }
}